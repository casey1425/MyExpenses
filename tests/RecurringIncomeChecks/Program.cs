using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action)
{
    try { await action(); throw new Exception("Invalid input accepted"); }
    catch (ArgumentException) { }
}

// 정기 수입 테이블이 없는 기존 DB를 업그레이드하는 경로와 신규 DB 경로를 모두 검증합니다.
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
            """);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
    // 오래된 DB는 앱과 같은 방식(옛 스키마 보강 → 기준선 → 이후 마이그레이션)으로 올립니다.
    if (legacy) { await DatabaseMigrator.MigrateExpensesAsync(db); await DatabaseMigrator.MigrateExpensesAsync(db); }
    else { await ExpensesSchema.EnsureCreatedAsync(db); await ExpensesSchema.EnsureCreatedAsync(db); }

    var factory = new TestFactory(options);
    var recurring = new RecurringIncomeService(factory);
    var incomes = new IncomeService(factory);
    static DateTime D(int y, int m, int d) => new(y, m, d);

    // 10일에 25일 월급 규칙을 등록하면 아직 도래하지 않아 생성하지 않습니다.
    await recurring.CreateAsync("A", D(2026, 10, 10), 25, 3_000_000, "급여", " 월급 ");
    await recurring.CreateAsync("B", D(2026, 10, 10), 1, 777, "용돈", "other");
    var rule = (await recurring.ListAsync("A")).Single();
    Check(rule.Memo == "월급" && rule.StartMonth == D(2026, 10, 1) && rule.IsActive, "Rule normalization failed");
    Check(await recurring.GenerateDueAsync("A", D(2026, 10, 10)) == 0, "Generated before due date");
    Check(await recurring.GenerateDueAsync("A", D(2026, 10, 24)) == 0, "Generated the day before");

    // 지정일 당일에 한 번만 생성하고, 반복 호출해도 중복되지 않습니다.
    Check(await recurring.GenerateDueAsync("A", D(2026, 10, 25)) == 1, "Not generated on due date");
    Check(await recurring.GenerateDueAsync("A", D(2026, 10, 25)) == 0 && await recurring.GenerateDueAsync("A", D(2026, 10, 31)) == 0, "Generated twice for the same month");
    var october = await incomes.ListAsync("A", D(2026, 10, 1));
    Check(october.Single() is { Amount: 3_000_000, Source: "급여", Memo: "월급" } generated && generated.Date == D(2026, 10, 25), "Generated income wrong");
    Check((await incomes.CashflowAsync("A", D(2026, 10, 1))).Income == 3_000_000, "Generated income missing from cashflow");

    // 오래 접속하지 않았다면 놓친 달을 모두 채웁니다.
    Check(await recurring.GenerateDueAsync("A", D(2026, 12, 31)) == 2, "Catch-up wrong");
    Check((await incomes.ListAsync("A", D(2026, 11, 1))).Single().Date == D(2026, 11, 25) && (await incomes.ListAsync("A", D(2026, 12, 1))).Single().Date == D(2026, 12, 25), "Catch-up dates wrong");

    // 사용자 격리: A 생성이 B에 영향을 주지 않고, B의 규칙은 B가 생성해야 합니다.
    Check((await incomes.ListAsync("B", D(2026, 10, 1))).Count == 0, "Generation leaked to B");
    Check(await recurring.GenerateDueAsync("B", D(2026, 10, 31)) == 1 && (await recurring.ListAsync("A")).Count == 1, "B generation wrong");

    // 29~31일은 해당 월 말일에 기록합니다. (2027년 2월은 28일까지)
    await recurring.CreateAsync("C", D(2027, 1, 31), 31, 100, "부수입", "");
    Check(await recurring.GenerateDueAsync("C", D(2027, 3, 1)) == 2, "Month-end generation count wrong");
    var cDates = (await incomes.ListAsync("C", D(2027, 1, 1))).Concat(await incomes.ListAsync("C", D(2027, 2, 1))).Select(i => i.Date).OrderBy(d => d).ToList();
    Check(cDates.SequenceEqual(new[] { D(2027, 1, 31), D(2027, 2, 28) }), "Month-end clamp wrong");
    Check(await recurring.GenerateDueAsync("C", D(2027, 3, 31)) == 1 && (await incomes.ListAsync("C", D(2027, 3, 1))).Single().Date == D(2027, 3, 31), "March end wrong");

    // 수정은 이미 생성된 수입을 바꾸지 않고 앞으로 생성될 값에만 적용합니다.
    Check(await recurring.UpdateAsync("A", rule.Id, 20, 3_500_000, "급여", "인상"), "Update failed");
    Check((await incomes.ListAsync("A", D(2026, 10, 1))).Single().Amount == 3_000_000, "Update rewrote past income");
    Check(await recurring.GenerateDueAsync("A", D(2027, 1, 31)) == 1, "Next month not generated after update");
    Check((await incomes.ListAsync("A", D(2027, 1, 1))).Single() is { Amount: 3_500_000, Memo: "인상" } updated && updated.Date == D(2027, 1, 20), "Updated rule not applied");
    Check(!await recurring.UpdateAsync("B", rule.Id, 1, 1, "급여", ""), "Foreign update accepted");

    // 중지한 기간은 소급하지 않고, 다시 시작한 달부터 생성합니다.
    Check(await recurring.SetActiveAsync("A", rule.Id, false, D(2027, 2, 10)), "Pause failed");
    Check(await recurring.GenerateDueAsync("A", D(2027, 4, 30)) == 0, "Paused rule generated");
    Check(!await recurring.SetActiveAsync("B", rule.Id, true, D(2027, 5, 10)), "Foreign resume accepted");
    Check(await recurring.SetActiveAsync("A", rule.Id, true, D(2027, 5, 10)), "Resume failed");
    Check(await recurring.GenerateDueAsync("A", D(2027, 5, 31)) == 1, "Resume should only generate the resumed month");
    Check((await incomes.ListAsync("A", D(2027, 2, 1))).Count == 0 && (await incomes.ListAsync("A", D(2027, 3, 1))).Count == 0 && (await incomes.ListAsync("A", D(2027, 4, 1))).Count == 0, "Paused months were back-filled");

    // 삭제는 규칙과 처리 이력만 지우고 이미 생성된 수입은 유지합니다. 타 계정은 삭제할 수 없습니다.
    var generatedBefore = (await incomes.ListAllAsync("A")).Count;
    Check(!await recurring.DeleteAsync("B", rule.Id), "Foreign delete accepted");
    Check(await recurring.DeleteAsync("A", rule.Id) && !await recurring.DeleteAsync("A", rule.Id), "Delete failed");
    Check((await recurring.ListAsync("A")).Count == 0 && await db.RecurringIncomeOccurrences.CountAsync(o => o.OwnerId == "A") == 0, "Delete left rule or history");
    Check((await incomes.ListAllAsync("A")).Count == generatedBefore, "Delete removed generated income");
    Check(await recurring.GenerateDueAsync("A", D(2027, 12, 31)) == 0, "Deleted rule generated");

    // 입력 검증
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 0, 100, "급여", ""));
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 32, 100, "급여", ""));
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 1, 0, "급여", ""));
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 1, -5, "급여", ""));
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 1, 100, "없는분류", ""));
    await Reject(() => recurring.CreateAsync("A", D(2026, 10, 1), 1, 100, "급여", new string('a', 101)));
    await Reject(() => recurring.CreateAsync("", D(2026, 10, 1), 1, 100, "급여", ""));
    await Reject(() => recurring.UpdateAsync("B", 1, 1, 100, "급여", new string('a', 101)));
    await Reject(() => recurring.ListAsync(" "));
    await Reject(() => recurring.GenerateDueAsync("", D(2026, 10, 1)));
    Check((await recurring.ListAsync("A")).Count == 0, "Invalid input created a rule");

    // 계정 삭제 시 규칙·처리 이력·수입을 모두 지우고 다른 계정은 유지합니다.
    await recurring.CreateAsync("A", D(2026, 10, 10), 1, 100, "급여", "again");
    await recurring.GenerateDueAsync("A", D(2026, 11, 1));
    await new UserDataDeletionService(factory).DeleteAsync("A");
    Check((await recurring.ListAsync("A")).Count == 0 && await db.RecurringIncomeOccurrences.CountAsync(o => o.OwnerId == "A") == 0 && (await incomes.ListAllAsync("A")).Count == 0, "Account deletion left data");
    Check((await recurring.ListAsync("B")).Count == 1 && (await incomes.ListAllAsync("B")).Count == 1 && (await recurring.ListAsync("C")).Count == 1, "Account deletion crossed owners");

    // 화면 로직: 정기 수입 화면과, 수입 기록 화면 접속 시 자동 생성되는 흐름을 확인합니다.
    // (InteractiveServer 페이지는 HtmlRenderer로 직접 렌더링할 수 없어 기존 Home 검증처럼 리플렉션을 씁니다.)
    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    var realToday = KoreanClock.Today;
    await recurring.CreateAsync("Z", realToday, 1, 2_000_000, "급여", "월급날");
    var auth = new TestAuth("Z");
    T Page<T>(Action<T> inject) where T : new() { var page = new T(); inject(page); return page; }
    static void Set(object target, string name, object? value) => target.GetType().GetProperty(name, flags)!.SetValue(target, value);
    static object? Field(object target, string name) => target.GetType().GetField(name, flags)!.GetValue(target);
    static void SetField(object target, string name, object? value) => target.GetType().GetField(name, flags)!.SetValue(target, value);
    static Task Invoke(object target, string method) => (Task)target.GetType().GetMethod(method, flags)!.Invoke(target, null)!;

    var rulesPage = Page<RecurringIncome>(page =>
    {
        Set(page, "AuthenticationStateProvider", auth);
        Set(page, "RecurringIncomeService", recurring);
        Set(page, "Logger", NullLogger<RecurringIncome>.Instance);
    });
    await Invoke(rulesPage, "OnInitializedAsync");
    Check(Field(rulesPage, "errorMessage") is null && ((List<RecurringIncomeRule>)Field(rulesPage, "rules")!).Single().Memo == "월급날", "Recurring income page did not load rules");
    Check((await incomes.ListAsync("Z", realToday)).Count == 1, "Opening the page did not generate due income");
    SetField(rulesPage, "dayOfMonth", 1); SetField(rulesPage, "amount", 0L);
    await Invoke(rulesPage, "CreateAsync");
    Check(Field(rulesPage, "errorMessage") is string && (await recurring.ListAsync("Z")).Count == 1, "Page accepted invalid amount");
    SetField(rulesPage, "amount", 50_000L); SetField(rulesPage, "source", "용돈"); SetField(rulesPage, "memo", "매월 용돈");
    await Invoke(rulesPage, "CreateAsync");
    Check(Field(rulesPage, "errorMessage") is null && (await recurring.ListAsync("Z")).Count == 2, "Page create failed");
    Check((await incomes.ListAsync("Z", realToday)).Count == 2, "Page create did not generate this month's income");
    SetField(rulesPage, "isBusy", false);
    auth.OwnerId = "Y"; // 로그인 계정이 바뀌면 저장하지 않습니다.
    SetField(rulesPage, "amount", 70_000L);
    await Invoke(rulesPage, "CreateAsync");
    Check(Field(rulesPage, "errorMessage") is string && (await recurring.ListAsync("Z")).Count == 2, "Identity change not blocked");
    auth.OwnerId = "Z";

    var incomePage = Page<Income>(page =>
    {
        Set(page, "AuthenticationStateProvider", auth);
        Set(page, "IncomeService", incomes);
        Set(page, "RecurringIncomeService", recurring);
        Set(page, "Logger", NullLogger<Income>.Instance);
    });
    await recurring.CreateAsync("Z", realToday, 1, 123_000, "부수입", "새 규칙");
    await Invoke(incomePage, "OnInitializedAsync");
    var shown = (List<IncomeRecord>)Field(incomePage, "incomes")!;
    Check(Field(incomePage, "error") is null && shown.Count == 3 && shown.Any(i => i.Memo == "새 규칙"), "Income page did not auto-generate recurring income");
    Check((await incomes.ListAsync("Z", realToday)).Count == 3, "Auto-generation not idempotent across page loads");

    Console.WriteLine($"PASS ({(legacy ? "upgraded" : "new")} DB): rules CRUD, due-date generation, catch-up, month-end clamp, pause/resume, no duplicates, ownership, validation and account deletion");
}
