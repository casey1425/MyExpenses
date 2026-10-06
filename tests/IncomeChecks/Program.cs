using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Charts;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action)
{
    try { await action(); throw new Exception("Invalid input accepted"); }
    catch (ArgumentException) { }
}

// 수입 테이블이 없는 기존 DB를 업그레이드하는 경로와 신규 DB 경로를 모두 검증합니다.
foreach (var legacy in new[] { false, true })
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
    await using var db = new ExpensesDbContext(options);
    if (legacy)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Expenses (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId TEXT NOT NULL, Date TEXT NOT NULL, Amount INTEGER NOT NULL, Category TEXT NOT NULL, Memo TEXT NOT NULL);
            INSERT INTO Expenses (OwnerId,Date,Amount,Category,Memo) VALUES ('A','2026-10-05 00:00:00',300000,'식비','existing');
            """);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
        db.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new(2026, 10, 5), Amount = 300000, Category = "식비", Memo = "existing" });
        await db.SaveChangesAsync();
    }
    // 오래된 DB는 앱과 같은 방식(옛 스키마 보강 → 기준선 → 이후 마이그레이션)으로 올립니다.
    if (legacy) { await DatabaseMigrator.MigrateExpensesAsync(db); await DatabaseMigrator.MigrateExpensesAsync(db); }
    else { await ExpensesSchema.EnsureCreatedAsync(db); await ExpensesSchema.EnsureCreatedAsync(db); }

    var factory = new TestFactory(options);
    var service = new IncomeService(factory);
    var october = new DateTime(2026, 10, 1);

    var salary = await service.AddAsync("A", new(new DateTime(2026, 10, 25, 13, 0, 0), 2_000_000, "급여", " 10월 급여 "));
    await service.AddAsync("A", new(new DateTime(2026, 10, 31), 100_000, "부수입", ""));
    await service.AddAsync("A", new(new DateTime(2026, 11, 1), 777, "용돈", "next month"));
    var foreign = await service.AddAsync("B", new(new DateTime(2026, 10, 10), 5_000_000, "급여", "other"));
    Check(salary.Memo == "10월 급여" && salary.Date == new DateTime(2026, 10, 25), "Normalization changed");

    // 월 경계: 10월 31일은 포함하고 11월 1일은 제외합니다.
    var list = await service.ListAsync("A", october);
    Check(list.Count == 2 && list[0].Source == "부수입", "Month range or ordering failed");
    Check((await service.ListAsync("A", new DateTime(2026, 11, 20))).Count == 1, "Next month listing failed");

    var cashflow = await service.CashflowAsync("A", october);
    Check(cashflow.Income == 2_100_000 && cashflow.Expense == 300_000 && cashflow.Net == 1_800_000, "Cashflow totals wrong");
    Check(cashflow.SavingsRate == 85.7, $"Savings rate wrong: {cashflow.SavingsRate}");
    var empty = await service.CashflowAsync("A", new DateTime(2026, 1, 1));
    Check(empty is { Income: 0, Expense: 0 } && empty.SavingsRate is null, "Empty month should have no savings rate");
    Check((await service.CashflowAsync("B", october)).Expense == 0, "Cashflow leaked other owner's expenses");
    Check(new MonthlyCashflow(100, 150).Net == -50 && new MonthlyCashflow(100, 150).SavingsRate == -50, "Negative net failed");

    // 소유권: 다른 계정의 수입은 수정·삭제할 수 없습니다.
    Check(await service.UpdateAsync("A", foreign.Id, new(october, 1, "급여", "")) is null, "Foreign update accepted");
    Check(!await service.DeleteAsync("A", foreign.Id), "Foreign delete accepted");
    Check((await service.ListAsync("B", october)).Count == 1, "Foreign record changed");

    var updated = await service.UpdateAsync("A", salary.Id, new(new DateTime(2026, 10, 26), 2_500_000, "급여", "보너스 포함"));
    Check(updated is { Amount: 2_500_000 } && (await service.CashflowAsync("A", october)).Income == 2_600_000, "Update failed");

    // ---- 월별 추이: 수입·지출·순수지 ----
    // 이 시점의 A: 10월 수입 2,500,000(10/26)+100,000(10/31), 11월 수입 777(11/1), 10월 지출 300,000(10/5)
    var trends = new ExpenseTrendsService(factory);
    var endOfOctober = await trends.LoadAsync("A", ExpenseTrends.MonthStart(new DateOnly(2026, 10, 1)), new DateOnly(2026, 10, 31));
    var cashflowMonths = endOfOctober.CashflowMonths ?? throw new Exception("Cashflow missing");
    Check(cashflowMonths.Count == 6 && cashflowMonths[^1].Month == new DateOnly(2026, 10, 1) && cashflowMonths[0].Month == new DateOnly(2026, 5, 1), "Cashflow months wrong");
    Check(cashflowMonths[^1] is { Income: 2_600_000, Expense: 300_000, Net: 2_300_000, IncomeCount: 2, IsPartial: true }, "Selected month cashflow wrong");
    Check(cashflowMonths[^1].SavingsRate == 88.5, $"Trend savings rate wrong: {cashflowMonths[^1].SavingsRate}");
    Check(cashflowMonths.Take(5).All(point => point is { Income: 0, Expense: 0 } && point.SavingsRate is null), "Empty months should be zero");

    // 이번 달은 오늘까지만 집계하므로 아직 오지 않은 수입은 제외합니다.
    var midOctober = await trends.LoadAsync("A", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20));
    Check(midOctober.CashflowMonths![^1] is { Income: 0, Net: -300_000 } && midOctober.CashflowMonths[^1].SavingsRate is null, "Partial month income should exclude future dates");

    var november = await trends.LoadAsync("A", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
    Check(november.CashflowMonths![^1] is { Income: 777, IncomeCount: 1 } && november.CashflowMonths[^2].Income == 2_600_000, "Month rollover wrong");
    var foreignTrend = await trends.LoadAsync("B", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
    Check(foreignTrend.CashflowMonths![^1] is { Income: 5_000_000, Expense: 0 }, "Trend leaked other owner's data");
    Check(ExpenseTrends.Calculate([], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)).CashflowMonths is null, "Cashflow should be optional");
    // Calculate를 직접 호출해도 구간 밖 수입(6개월 이전, 오늘 이후)은 집계하지 않습니다.
    var direct = ExpenseTrends.Calculate([], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), null,
    [
        new IncomeRecord { Date = new(2026, 4, 30), Amount = 111 },
        new IncomeRecord { Date = new(2026, 5, 1), Amount = 222 },
        new IncomeRecord { Date = new(2026, 10, 20), Amount = 333 },
        new IncomeRecord { Date = new(2026, 10, 21), Amount = 444 }
    ]).CashflowMonths!;
    Check(direct[0].Income == 222 && direct[^1].Income == 333 && direct.Sum(point => point.Income) == 555, "Calculate range filter wrong");

    // 차트 컴포넌트를 실제로 렌더링해 마크업 오류와 표시 문구를 확인합니다.
    await using var renderServices = new ServiceCollection().AddLogging().BuildServiceProvider();
    await using var htmlRenderer = new HtmlRenderer(renderServices, NullLoggerFactory.Instance);
    async Task<string> RenderChart(IReadOnlyList<MonthlyCashflowPoint> months) => await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        (await htmlRenderer.RenderComponentAsync<CashflowChart>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Months"] = months }))).ToHtmlString());
    var chartHtml = await RenderChart(cashflowMonths);
    Check(chartHtml.Contains("<svg") && chartHtml.Contains("2,600,000원") && chartHtml.Contains("88.5%") && chartHtml.Contains("오늘까지"), "Cashflow chart content missing");
    Check((await RenderChart(midOctober.CashflowMonths!)).Contains("-300,000원"), "Negative net not rendered");
    var emptyChart = await RenderChart(ExpenseTrends.Calculate([], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null, []).CashflowMonths!);
    Check(!emptyChart.Contains("<svg") && emptyChart.Contains("href=\"/income\""), "Empty cashflow state missing");

    // ---- 수입 CSV 내보내기·가져오기 ----
    db.Incomes.Add(new IncomeRecord { OwnerId = "A", Date = new(2026, 9, 3), Amount = 50_000, Source = "용돈", Memo = "=SUM(A1), \"quoted\"" });
    await db.SaveChangesAsync();
    var exported = IncomeCsvExporter.Create(await service.ListAllAsync("A"));
    Check(exported.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "BOM missing");
    var csvText = System.Text.Encoding.UTF8.GetString(exported).TrimStart('\uFEFF');
    Check(csvText.StartsWith("날짜,금액(원),분류,메모\r\n") && csvText.Contains("2026-10-31,100000,\"부수입\",\"\""), "Export format wrong");
    Check(csvText.Contains("\"\t=SUM(A1), \"\"quoted\"\"\""), "Formula text not protected");
    Check((await service.ListAllAsync("A")).Count == 4 && !csvText.Contains("5000000"), "Export scope wrong");

    var parsed = IncomeCsvImporter.Parse(new StringReader(csvText));
    Check(parsed.Issues.Count == 0 && parsed.Rows.Count == 4, "Round-trip parse failed");
    Check(parsed.Rows.Any(row => row.Memo == "=SUM(A1), \"quoted\""), "Formula protection not removed on import");

    var csvService = new IncomeCsvImportService(factory);
    var ownPreview = await csvService.PreviewAsync("A", parsed.Rows);
    Check(ownPreview.All(candidate => candidate.IsDuplicate), "Own export should be all duplicates");
    Check(await csvService.ImportAsync("A", parsed.Rows, includeDuplicates: false) == 0, "Duplicates were imported");
    var otherPreview = await csvService.PreviewAsync("C", parsed.Rows);
    Check(otherPreview.All(candidate => !candidate.IsDuplicate), "Other owner's data caused duplicates");
    Check(await csvService.ImportAsync("C", parsed.Rows, includeDuplicates: false) == 4, "Import into new owner failed");
    Check((await service.ListAllAsync("C")).Count == 4 && (await service.ListAllAsync("A")).Count == 4 && (await service.ListAllAsync("B")).Count == 1, "Import crossed owners");

    var withFileDuplicate = IncomeCsvImporter.Parse(new StringReader("날짜,금액(원),분류,메모\r\n2026-01-05,1000,급여,a\r\n2026-01-05,1000,급여,a\r\n")).Rows;
    Check((await csvService.PreviewAsync("D", withFileDuplicate)).Select(c => c.IsDuplicate).SequenceEqual(new[] { false, true }), "In-file duplicate not detected");
    Check(await csvService.ImportAsync("D", withFileDuplicate, includeDuplicates: false) == 1, "In-file duplicate imported");
    Check(await csvService.ImportAsync("D", withFileDuplicate, includeDuplicates: true) == 2, "includeDuplicates ignored");

    // 잘못된 행이 하나라도 있으면 아무것도 저장하지 않습니다.
    var beforeBad = await db.Incomes.CountAsync();
    await Reject(() => csvService.ImportAsync("E", [new(2, new DateTime(2026, 1, 1), 100, "급여", ""), new(3, new DateTime(2026, 1, 2), 0, "급여", "")], false));
    await Reject(() => csvService.ImportAsync("E", [new(2, new DateTime(2026, 1, 1), 100, "없는분류", "")], false));
    await Reject(() => csvService.ImportAsync("E", [], false));
    await Reject(() => csvService.ImportAsync("E", Enumerable.Range(2, 1001).Select(i => new IncomeCsvRow(i, new DateTime(2026, 1, 1), 1, "급여", "")).ToList(), false));
    await Reject(() => csvService.ImportAsync("", withFileDuplicate, false));
    Check(await db.Incomes.CountAsync() == beforeBad, "Partial import saved rows");

    static IncomeCsvParseResult ParseCsv(string body) => IncomeCsvImporter.Parse(new StringReader("날짜,금액(원),분류,메모\r\n" + body));
    Check(IncomeCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모\r\n")).Issues.Single().RowNumber == 1, "Wrong header accepted");
    Check(IncomeCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모,결제수단,결제유형\r\n")).Rows.Count == 0, "Expense CSV accepted as income");
    foreach (var bad in new[]
    {
        "2026-13-01,100,급여,x", "26-1-1,100,급여,x", "2026-01-01,abc,급여,x", "2026-01-01,-5,급여,x", "2026-01-01,0,급여,x",
        "2026-01-01,1.5,급여,x", "2026-01-01,1,000,급여,x", "2026-01-01,100,없는분류,x", $"2026-01-01,100,급여,{new string('a', 101)}", "2026-01-01,100,급여"
    })
    {
        var result = ParseCsv(bad + "\r\n");
        Check(result.Rows.Count == 0 && result.Issues.Count == 1 && result.Issues[0].RowNumber == 2, $"Bad row accepted: {bad}");
    }
    Check(ParseCsv("2026-01-01,100,급여,\"unterminated\r\n").Issues.Count == 1, "Unclosed quote accepted");
    Check(ParseCsv("").Issues.Single().Message.Contains("가져올 수입 기록이 없습니다"), "Empty CSV accepted");
    Check(ParseCsv(string.Concat(Enumerable.Repeat("2026-01-01,1,급여,\r\n", 1001))).Issues.Any(i => i.Message.Contains("최대")), "Row limit not enforced");
    Check(ParseCsv("2026-01-01,100,\"이자·투자\",\" 공백 메모 \"\r\n").Rows.Single() is { Source: "이자·투자", Memo: "공백 메모" }, "Valid quoted row failed");

    await Reject(() => service.AddAsync("A", new(default, 100, "급여", "")));
    await Reject(() => service.AddAsync("A", new(october, 0, "급여", "")));
    await Reject(() => service.AddAsync("A", new(october, -5, "급여", "")));
    await Reject(() => service.AddAsync("A", new(october, 100, "존재하지 않는 분류", "")));
    await Reject(() => service.AddAsync("A", new(october, 100, "급여", new string('a', 101))));
    await Reject(() => service.AddAsync("", new(october, 100, "급여", "")));
    await Reject(() => service.ListAsync(" ", october));

    Check(await service.DeleteAsync("A", salary.Id) && !await service.DeleteAsync("A", salary.Id), "Delete failed");

    await new UserDataDeletionService(factory).DeleteAsync("A");
    Check((await service.ListAsync("A", october)).Count == 0 && (await service.ListAsync("B", october)).Count == 1, "Account deletion isolation failed");
    Console.WriteLine($"PASS ({(legacy ? "upgraded" : "new")} DB): CRUD, validation, ownership, month boundaries, cashflow, trends, CSV export/import and account deletion");
}
