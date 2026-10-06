using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MyExpenses;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Reject(Func<Task> action, string message = "Invalid input accepted")
{
    try { await action(); throw new Exception(message); }
    catch (ArgumentException) { }
}
static DateTime D(int y, int m, int d) => new(y, m, d);

var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(9));

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using (var init = new ExpensesDbContext(options))
    await DatabaseMigrator.MigrateExpensesAsync(init);   // 앱과 같은 방식(마이그레이션)으로 스키마를 만듭니다.
var factory = new TestFactory(options);
var backup = new BackupService(factory);
var categoriesService = new CategoryService(factory);

async Task<ExpensesDbContext> Db() { await Task.CompletedTask; return new ExpensesDbContext(options); }

// ---- 시드: A 계정(모든 종류의 데이터, 정당한 중복 포함) ----
async Task SeedAsync(string owner)
{
    await categoriesService.ListAsync(owner);                       // 기본 카테고리 6개
    await categoriesService.SaveAsync(owner, null, "여행");
    var etc = (await categoriesService.ListAsync(owner)).Single(c => c.Name == "기타");
    await categoriesService.ArchiveAsync(owner, etc.Id, true);
    await using var db = await Db();
    var card = new PaymentMethod { OwnerId = owner, Name = "국민 체크카드", Type = "체크카드" };
    var cash = new PaymentMethod { OwnerId = owner, Name = "현금", Type = "현금" };
    db.PaymentMethods.AddRange(card, cash, new PaymentMethod { OwnerId = owner, Name = "예비 카드", Type = "신용카드" });
    await db.SaveChangesAsync();
    db.Expenses.AddRange(
        new ExpenseRecord { OwnerId = owner, Date = D(2026, 9, 1), Amount = 12_000, Category = "식비", Memo = "점심", PaymentMethodId = card.Id },
        new ExpenseRecord { OwnerId = owner, Date = D(2026, 9, 2), Amount = 4_500, Category = "카페", Memo = "커피" },
        new ExpenseRecord { OwnerId = owner, Date = D(2026, 9, 2), Amount = 4_500, Category = "카페", Memo = "커피" },   // 같은 날 같은 커피 2잔
        new ExpenseRecord { OwnerId = owner, Date = D(2026, 9, 5), Amount = 9_900, Category = "식비", Memo = "구독" },
        new ExpenseRecord { OwnerId = owner, Date = D(2026, 10, 1), Amount = 30_000, Category = "여행", Memo = "기차", PaymentMethodId = cash.Id });
    db.ExpenseTemplates.Add(new ExpenseTemplate { OwnerId = owner, Name = "커피", Amount = 4_500, Category = "카페", Memo = "커피", PaymentMethodId = card.Id });
    db.MonthlyBudgets.AddRange(new MonthlyBudget { OwnerId = owner, Month = D(2026, 9, 1), Amount = 500_000 }, new MonthlyBudget { OwnerId = owner, Month = D(2026, 10, 1), Amount = 600_000 });
    db.CategoryBudgets.Add(new CategoryBudget { OwnerId = owner, Month = D(2026, 9, 1), Category = "식비", Amount = 200_000 });
    var rule = new RecurringExpenseRule { OwnerId = owner, StartMonth = D(2026, 9, 1), DayOfMonth = 5, Amount = 9_900, Category = "식비", Memo = "구독", IsActive = true };
    var stopped = new RecurringExpenseRule { OwnerId = owner, StartMonth = D(2026, 10, 1), DayOfMonth = 31, Amount = 1_000, Category = "생활", Memo = "중지됨", IsActive = false };
    db.RecurringExpenseRules.AddRange(rule, stopped);
    db.Incomes.AddRange(
        new IncomeRecord { OwnerId = owner, Date = D(2026, 9, 25), Amount = 3_000_000, Source = "급여", Memo = "월급" },
        new IncomeRecord { OwnerId = owner, Date = D(2026, 9, 26), Amount = 100_000, Source = "부수입", Memo = "" },
        new IncomeRecord { OwnerId = owner, Date = D(2026, 9, 26), Amount = 100_000, Source = "부수입", Memo = "" });
    var incomeRule = new RecurringIncomeRule { OwnerId = owner, StartMonth = D(2026, 9, 1), DayOfMonth = 25, Amount = 3_000_000, Source = "급여", Memo = "월급", IsActive = true };
    db.RecurringIncomeRules.Add(incomeRule);
    var trip = new SavingsGoal { OwnerId = owner, Name = "여행", TargetAmount = 3_000_000, TargetDate = D(2026, 12, 31), CreatedDate = D(2026, 9, 1) };
    var emergency = new SavingsGoal { OwnerId = owner, Name = "비상금", TargetAmount = 1_000_000, CreatedDate = D(2026, 9, 15) };
    db.SavingsGoals.AddRange(trip, emergency);
    await db.SaveChangesAsync();
    db.RecurringExpenseOccurrences.Add(new RecurringExpenseOccurrence { OwnerId = owner, RuleId = rule.Id, Month = D(2026, 9, 1) });
    db.RecurringIncomeOccurrences.Add(new RecurringIncomeOccurrence { OwnerId = owner, RuleId = incomeRule.Id, Month = D(2026, 9, 1) });
    db.SavingsDeposits.AddRange(
        new SavingsDeposit { OwnerId = owner, GoalId = trip.Id, Date = D(2026, 9, 10), Amount = 500_000, Memo = "첫 저축" },
        new SavingsDeposit { OwnerId = owner, GoalId = trip.Id, Date = D(2026, 9, 20), Amount = 300_000, Memo = "" });
    await db.SaveChangesAsync();
}

async Task<byte[]> ExportBytes(string owner) => BackupSerializer.Serialize(await backup.ExportAsync(owner, now));
async Task<BackupFile> Export(string owner) => await backup.ExportAsync(owner, now);
// 카테고리 섹션을 제외하고 비교할 때 쓰는 바이트 표현
static byte[] WithoutCategories(BackupFile file)
{
    var data = file.Data with { Categories = [] };
    return BackupSerializer.Serialize(file with { Data = data, Counts = BackupCounts.Of(data) });
}
static string Summary(BackupRestoreReport report) =>
    string.Join(";", report.Sections.Select(s => $"{s.Name}:{s.Total}/{s.Added}/{s.Skipped}")) + "|" + string.Join(";", report.Warnings) + "|" + report.DeletedRows;

await SeedAsync("A");
// 다른 사용자의 데이터(격리 확인용)
await using (var other = await Db())
{
    other.Expenses.Add(new ExpenseRecord { OwnerId = "B", Date = D(2026, 9, 9), Amount = 777, Category = "식비", Memo = "B의 지출" });
    other.Incomes.Add(new IncomeRecord { OwnerId = "B", Date = D(2026, 9, 9), Amount = 888, Source = "급여", Memo = "B의 수입" });
    await other.SaveChangesAsync();
}
var bytesA = await ExportBytes("A");
var bytesB = await ExportBytes("B");
var fileA = await Export("A");

// ---- 1. 내보내기 ----
Check(fileA.Counts == new BackupCounts(7, 3, 5, 1, 2, 1, 2, 3, 1, 2, 2), $"Export counts wrong: {fileA.Counts}");
Check(fileA.Format == "MyExpensesBackup" && fileA.Version == BackupFile.CurrentVersion && BackupFile.CurrentVersion == 2 && fileA.ExportedAt == now, "Export header wrong");
var secondExportA = await ExportBytes("A");
Check(bytesA.SequenceEqual(secondExportA), "Export is not deterministic");
var json = Encoding.UTF8.GetString(bytesA);
Check(!json.Contains("ownerId", StringComparison.OrdinalIgnoreCase) && !json.Contains("\"A\"") && !json.Contains("B의"), "Export leaked owner id or another user's data");
Check(json.Contains("국민 체크카드") && !json.Contains("\\u"), "Export should keep Korean readable");
Check(fileA.Data.Categories.Single(c => c.Name == "기타").IsArchived && fileA.Data.Categories.Any(c => c.Name == "여행"), "Categories not exported");
Check(fileA.Data.Expenses.Count(e => e is { Date.Month: 9, Date.Day: 2, Amount: 4_500 }) == 2, "Legitimate duplicates must be exported separately");
Check(fileA.Data.Expenses[0].PaymentMethod == "국민 체크카드" && fileA.Data.Expenses.Any(e => e.PaymentMethod is null), "Payment method references wrong");
Check(fileA.Data.RecurringExpenses.Single(r => r.IsActive).GeneratedMonths.SequenceEqual(new[] { "2026-09" }) && fileA.Data.RecurringExpenses.Single(r => !r.IsActive).GeneratedMonths.Count == 0, "Generated months not exported");
Check(fileA.Data.SavingsGoals.Single(g => g.Name == "여행").Deposits.Count == 2 && fileA.Data.SavingsGoals.Single(g => g.Name == "비상금").TargetDate is null, "Goals not exported");
Check((await Export("nobody")).Counts.Total == 0, "Empty account export should be empty");
Check(BackupSerializer.Parse(bytesA) is { File: not null }, "Own export must parse and validate");
await Reject(() => backup.ExportAsync(" ", now));

// ---- 2. 덮어쓰기 복원: 다른 계정으로 옮기면 내보내기가 바이트 단위로 같다 ----
var parsed = BackupSerializer.Parse(bytesA).File!;
var replaceC = await backup.RestoreAsync("C", parsed, BackupRestoreMode.Replace, dryRun: false);
Check(replaceC.Sections.All(s => s.Added == s.Total && s.Skipped == 0) && replaceC.DeletedRows == 0 && replaceC.Warnings.Count == 0 && !replaceC.DryRun, $"Replace into empty account report wrong: {Summary(replaceC)}");
Check((await ExportBytes("C")).SequenceEqual(bytesA), "Restored account's export differs from the original backup");
Check((await ExportBytes("A")).SequenceEqual(bytesA) && (await ExportBytes("B")).SequenceEqual(bytesB), "Restore changed another account");

// ---- 3. 병합 복원: 새 계정(기본 카테고리만 있음)에 병합 ----
var mergeD = await backup.RestoreAsync("D", parsed, BackupRestoreMode.Merge, dryRun: false);
Check(mergeD.Sections.Single(s => s.Name == "카테고리") is { Total: 7, Added: 1, Skipped: 6 }, "Merge should keep default categories and add only new ones");
Check(mergeD.Sections.Where(s => s.Name != "카테고리").All(s => s.Added == s.Total), "Merge into fresh account should add everything");
Check(WithoutCategories(await Export("D")).SequenceEqual(WithoutCategories(fileA)), "Merged data differs from the backup");
Check((await Export("D")).Data.Categories.Single(c => c.Name == "기타").IsArchived == false, "Merge must not change existing categories");

// ---- 4. 반복 복원은 늘어나지 않는다(정당한 중복은 보존) ----
var again = await backup.RestoreAsync("D", parsed, BackupRestoreMode.Merge, dryRun: false);
Check(again.TotalAdded == 0 && again.Sections.All(s => s.Skipped == s.Total), $"Second restore added data: {Summary(again)}");
Check(WithoutCategories(await Export("D")).SequenceEqual(WithoutCategories(fileA)), "Repeated restore changed the data");
Check((await Export("D")).Data.Expenses.Count(e => e is { Date.Day: 2, Amount: 4_500 }) == 2, "Duplicate expenses were collapsed or tripled");

// ---- 5. 일부가 사라진 뒤 병합하면 빠진 것만 채운다 ----
await using (var db = await Db())
{
    var coffee = await db.Expenses.Where(e => e.OwnerId == "D" && e.Memo == "커피").OrderBy(e => e.Id).FirstAsync();
    db.Expenses.Remove(coffee);                                                          // 같은 커피 2잔 중 1잔만 삭제
    db.Incomes.Remove(await db.Incomes.FirstAsync(i => i.OwnerId == "D" && i.Source == "급여" && i.Memo == "월급"));
    var trip = await db.SavingsGoals.SingleAsync(g => g.OwnerId == "D" && g.Name == "여행");
    db.SavingsDeposits.RemoveRange(db.SavingsDeposits.Where(d => d.GoalId == trip.Id));
    db.SavingsGoals.Remove(trip);                                                        // 목표와 저축 내역 삭제
    await db.SaveChangesAsync();
}
var refill = await backup.RestoreAsync("D", parsed, BackupRestoreMode.Merge, dryRun: false);
Check(refill.Sections.Single(s => s.Name == "지출") is { Added: 1, Skipped: 4 } && refill.Sections.Single(s => s.Name == "수입") is { Added: 1 }
      && refill.Sections.Single(s => s.Name == "목표 저축") is { Added: 1 } && refill.Sections.Single(s => s.Name == "저축 내역") is { Added: 2 }
      && refill.Sections.Where(s => s.Name is not ("지출" or "수입" or "목표 저축" or "저축 내역")).All(s => s.Added == 0), $"Partial-loss merge wrong: {Summary(refill)}");
Check(WithoutCategories(await Export("D")).SequenceEqual(WithoutCategories(fileA)), "Data not fully restored after partial loss");

// ---- 6. 미리보기(dry run)는 실제 결과와 같고 아무것도 바꾸지 않는다 ----
var dry = await backup.RestoreAsync("E", parsed, BackupRestoreMode.Merge, dryRun: true);
Check(dry.DryRun && (await Export("E")).Counts.Total == 0, "Dry run changed the database");
await using (var db = await Db())
    Check(!await db.UserCategories.AnyAsync(c => c.OwnerId == "E"), "Dry run left seeded categories behind");
var real = await backup.RestoreAsync("E", parsed, BackupRestoreMode.Merge, dryRun: false);
Check(Summary(dry) == Summary(real), $"Dry run report differs from the real run:\n{Summary(dry)}\n{Summary(real)}");

// ---- 7. 덮어쓰기는 파일에 없는 현재 데이터를 지운다 ----
await using (var db = await Db())
{
    db.Expenses.Add(new ExpenseRecord { OwnerId = "E", Date = D(2026, 10, 3), Amount = 1, Category = "식비", Memo = "덮어쓰면 사라질 지출" });
    db.ExpenseTemplates.Add(new ExpenseTemplate { OwnerId = "E", Name = "임시", Amount = 1, Category = "식비", Memo = "" });
    // 백업에 없는 현재 데이터가 모든 종류의 테이블에 남아 있어야, 덮어쓰기가 하나라도 지우지 않으면 잡아낼 수 있습니다.
    var extraGoal = new SavingsGoal { OwnerId = "E", Name = "임시 목표", TargetAmount = 1, CreatedDate = D(2026, 10, 3) };
    db.SavingsGoals.Add(extraGoal);
    db.UserCategories.Add(new UserCategory { OwnerId = "E", Name = "임시 카테고리", Position = 99 });
    db.PaymentMethods.Add(new PaymentMethod { OwnerId = "E", Name = "임시 결제", Type = "기타" });
    db.Incomes.Add(new IncomeRecord { OwnerId = "E", Date = D(2026, 10, 3), Amount = 1, Source = "기타", Memo = "덮어쓰면 사라질 수입" });
    db.MonthlyBudgets.Add(new MonthlyBudget { OwnerId = "E", Month = D(2027, 1, 1), Amount = 1 });
    db.CategoryBudgets.Add(new CategoryBudget { OwnerId = "E", Month = D(2027, 1, 1), Category = "식비", Amount = 1 });
    var extraExpenseRule = new RecurringExpenseRule { OwnerId = "E", StartMonth = D(2026, 10, 1), DayOfMonth = 1, Amount = 1, Category = "식비", Memo = "임시 규칙", IsActive = true };
    var extraIncomeRule = new RecurringIncomeRule { OwnerId = "E", StartMonth = D(2026, 10, 1), DayOfMonth = 1, Amount = 1, Source = "기타", Memo = "임시 규칙", IsActive = true };
    db.RecurringExpenseRules.Add(extraExpenseRule);
    db.RecurringIncomeRules.Add(extraIncomeRule);
    await db.SaveChangesAsync();
    db.RecurringExpenseOccurrences.Add(new RecurringExpenseOccurrence { OwnerId = "E", RuleId = extraExpenseRule.Id, Month = D(2026, 10, 1) });
    db.RecurringIncomeOccurrences.Add(new RecurringIncomeOccurrence { OwnerId = "E", RuleId = extraIncomeRule.Id, Month = D(2026, 10, 1) });
    db.SavingsDeposits.Add(new SavingsDeposit { OwnerId = "E", GoalId = extraGoal.Id, Date = D(2026, 10, 3), Amount = 1, Memo = "임시 저축" });
    await db.SaveChangesAsync();
}
var replaceE = await backup.RestoreAsync("E", parsed, BackupRestoreMode.Replace, dryRun: true);
var afterPreview = await ExportBytes("E");
Check(replaceE.DeletedRows > 10 && !afterPreview.SequenceEqual(bytesA), "Replace preview should report deletions without applying them");
await backup.RestoreAsync("E", parsed, BackupRestoreMode.Replace, dryRun: false);
Check((await ExportBytes("E")).SequenceEqual(bytesA), "Replace did not produce exactly the backup contents");
// 내보내기에는 보이지 않는 연결 행(생성 이력·저축 내역)도 고아로 남지 않아야 합니다.
await using (var db = await Db())
    Check(await db.RecurringExpenseOccurrences.CountAsync(o => o.OwnerId == "E") == 1
          && await db.RecurringIncomeOccurrences.CountAsync(o => o.OwnerId == "E") == 1
          && await db.SavingsDeposits.CountAsync(d => d.OwnerId == "E") == 2, "Replace left orphaned history or deposit rows");
Check((await ExportBytes("C")).SequenceEqual(bytesA) && (await ExportBytes("B")).SequenceEqual(bytesB), "Replace affected another account");

// ---- 8. 결제수단은 이름으로 연결하고 유형이 다르면 기존 항목을 유지한 채 알린다 ----
await using (var db = await Db())
{
    db.PaymentMethods.Add(new PaymentMethod { OwnerId = "F", Name = "현금", Type = "기타" });
    await db.SaveChangesAsync();
}
var mergeF = await backup.RestoreAsync("F", parsed, BackupRestoreMode.Merge, dryRun: false);
Check(mergeF.Warnings.Count == 1 && mergeF.Warnings[0].Contains("현금") && mergeF.Sections.Single(s => s.Name == "결제수단") is { Added: 2, Skipped: 1 }, $"Method conflict not reported: {Summary(mergeF)}");
var exportF = await Export("F");
Check(exportF.Data.PaymentMethods.Single(m => m.Name == "현금").Type == "기타" && exportF.Data.Expenses.Any(e => e is { Category: "여행", PaymentMethod: "현금" }), "Existing payment method not reused");

// ---- 9. 정기 규칙의 생성 이력을 복원하므로 과거 달이 다시 생성되지 않는다 ----
var generatedExpenses = await new RecurringExpenseService(factory).GenerateDueAsync("C", D(2026, 10, 31));
var generatedIncomes = await new RecurringIncomeService(factory).GenerateDueAsync("C", D(2026, 10, 31));
Check(generatedExpenses == 1 && generatedIncomes == 1, $"Only the missing month (Oct) should be generated: {generatedExpenses}/{generatedIncomes}");
await using (var db = await Db())
{
    Check(await db.Expenses.CountAsync(e => e.OwnerId == "C" && e.Memo == "구독" && e.Date.Month == 9) == 1, "September recurring expense was duplicated");
    Check(await db.Incomes.CountAsync(i => i.OwnerId == "C" && i.Memo == "월급" && i.Date.Month == 9) == 1, "September recurring income was duplicated");
}

// ---- 10. 실패하면 아무것도 바뀌지 않는다 ----
// (a) 목표 개수 제한 초과
await using (var db = await Db())
{
    for (var i = 0; i < SavingsGoalService.MaxGoals - 1; i++)
        db.SavingsGoals.Add(new SavingsGoal { OwnerId = "H", Name = "기존 목표" + i, TargetAmount = 1000, CreatedDate = D(2026, 9, 1) });
    await db.SaveChangesAsync();
}
var beforeH = await ExportBytes("H");
await Reject(() => backup.RestoreAsync("H", parsed, BackupRestoreMode.Merge, dryRun: false), "Goal limit not enforced");
Check((await ExportBytes("H")).SequenceEqual(beforeH), "Failed merge left partial data");
// (b) 저장 중간에 예외가 발생하는 상황(덮어쓰기여도 기존 데이터가 지워지면 안 됨)
var failing = new FailOnNthSave(4);
var failingOptions = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).AddInterceptors(failing).Options;
var failingBackup = new BackupService(new TestFactory(failingOptions));
var beforeE = await ExportBytes("E");
try { await failingBackup.RestoreAsync("E", parsed, BackupRestoreMode.Replace, dryRun: false); throw new Exception("Injected failure did not happen"); }
catch (InvalidOperationException ex) when (ex.Message == "injected") { }
Check(failing.Calls >= 4 && (await ExportBytes("E")).SequenceEqual(beforeE), "Replace was not rolled back after a mid-way failure");
// 병합은 저장을 5번 합니다. 앞쪽(2번째)과 마지막(5번째) 저장이 실패해도 모두 되돌려져야 합니다.
foreach (var failOn in new[] { 2, 5 })
{
    var failingMerge = new FailOnNthSave(failOn);
    try { await new BackupService(new TestFactory(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).AddInterceptors(failingMerge).Options)).RestoreAsync("G", parsed, BackupRestoreMode.Merge, dryRun: false); throw new Exception($"Injected failure #{failOn} did not happen"); }
    catch (InvalidOperationException ex) when (ex.Message == "injected") { }
    Check((await Export("G")).Counts.Total == 0, $"Merge was not rolled back after failure at save #{failOn}");
    await using var check = await Db();
    Check(!await check.UserCategories.AnyAsync(c => c.OwnerId == "G"), $"Seeded categories survived the rollback at save #{failOn}");
}
// (c) 잘못된 파일을 직접 넘겨도 서비스가 다시 검증한다
await Reject(() => backup.RestoreAsync("G", parsed with { Data = parsed.Data with { Expenses = [parsed.Data.Expenses[0] with { Amount = 0 }, .. parsed.Data.Expenses.Skip(1)] } }, BackupRestoreMode.Merge, dryRun: false), "Service accepted an invalid file");
await Reject(() => backup.RestoreAsync(" ", parsed, BackupRestoreMode.Merge, dryRun: false));
Check((await Export("G")).Counts.Total == 0, "Invalid restore changed data");

// ---- 11. 카테고리 목록에 없는 이름이 기록에 남은 과거 데이터도 내보내고 복원할 수 있다 ----
await using (var db = await Db())
{
    db.Expenses.Add(new ExpenseRecord { OwnerId = "I", Date = D(2025, 1, 1), Amount = 100, Category = "옛카테고리", Memo = "" });
    await db.SaveChangesAsync();
}
var legacyFile = BackupSerializer.Parse(await ExportBytes("I"));
Check(legacyFile.File is not null && legacyFile.File.Data.Categories.Any(c => c.Name == "옛카테고리"), "Referenced-but-missing category not exported");
await backup.RestoreAsync("J", legacyFile.File!, BackupRestoreMode.Merge, dryRun: false);
Check((await Export("J")).Data.Categories.Any(c => c.Name == "옛카테고리") && (await Export("J")).Data.Expenses.Single().Category == "옛카테고리", "Legacy category not restored");

// ---- 12. 내보내기 엔드포인트: 헤더·본문·인증 ----
static DefaultHttpContext EndpointContext(string? owner)
{
    var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
    context.Response.Body = new MemoryStream();
    if (owner is not null)
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "test"));
    return context;
}
var okContext = EndpointContext("A");
var okResult = await BackupEndpoints.ExportAsync(okContext, backup, CancellationToken.None);
await okResult.ExecuteAsync(okContext);
okContext.Response.Body.Position = 0;
var body = ((MemoryStream)okContext.Response.Body).ToArray();
Check(okContext.Response.Headers.CacheControl == "no-store" && okContext.Response.Headers["X-Content-Type-Options"] == "nosniff", "Sensitive download must not be cacheable or sniffable");
Check(okContext.Response.ContentType == "application/json; charset=utf-8", $"Content type wrong: {okContext.Response.ContentType}");
var disposition = okContext.Response.Headers.ContentDisposition.ToString();
Check(disposition.Contains("attachment") && disposition.Contains("MyExpenses-backup-") && disposition.Contains(".json"), $"Download file name wrong: {disposition}");
var served = BackupSerializer.Parse(body);
Check(served.File is not null && served.File.Counts == fileA.Counts && served.File.Data.Expenses.Select(e => (e.Date, e.Amount, e.Category, e.Memo, e.PaymentMethod, string.Join("|", e.Tags ?? []))).SequenceEqual(fileA.Data.Expenses.Select(e => (e.Date, e.Amount, e.Category, e.Memo, e.PaymentMethod, string.Join("|", e.Tags ?? [])))), "Endpoint body is not the user's backup");
var denied = EndpointContext(null);
var deniedResult = await BackupEndpoints.ExportAsync(denied, backup, CancellationToken.None);
await deniedResult.ExecuteAsync(denied);
Check(denied.Response.StatusCode == 401, $"Anonymous export should be 401, was {denied.Response.StatusCode}");

// ---- 12-2. 화면 로직(리플렉션): 파일 검증 → 미리보기 → 모드 전환 → 확인 문구 → 복원 ----
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var pageAuth = new TestAuth("P");
var page = new MyExpenses.Components.Pages.Backup();
void SetProp(string name, object? value) => typeof(MyExpenses.Components.Pages.Backup).GetProperty(name, flags)!.SetValue(page, value);
object? Field(string name) => typeof(MyExpenses.Components.Pages.Backup).GetField(name, flags)!.GetValue(page);
void SetField(string name, object? value) => typeof(MyExpenses.Components.Pages.Backup).GetField(name, flags)!.SetValue(page, value);
bool CanApply() => (bool)typeof(MyExpenses.Components.Pages.Backup).GetProperty("CanApply", flags)!.GetValue(page)!;
Task Call(string method, params object[] args) => (Task)typeof(MyExpenses.Components.Pages.Backup).GetMethod(method, flags)!.Invoke(page, args)!;
void ResetPage() => typeof(MyExpenses.Components.Pages.Backup).GetMethod("Reset", flags)!.Invoke(page, null);
SetProp("AuthenticationStateProvider", pageAuth); SetProp("BackupService", backup); SetProp("Logger", NullLogger<MyExpenses.Components.Pages.Backup>.Instance);
SetField("ownerId", "P");
IReadOnlyList<string> PageIssues() => (IReadOnlyList<string>)Field("issues")!;

await Call("LoadAsync", new MemoryStream(Encoding.UTF8.GetBytes("{ 이건 백업이 아님")), "bad.json");
Check(PageIssues().Count == 1 && Field("backup") is null && Field("preview") is null, "Page accepted a non-backup file");
ResetPage();
await Call("LoadAsync", new MemoryStream(new byte[BackupLimits.MaxFileBytes + 1]), "huge.json");
Check(PageIssues().Single().Contains("너무 큽니다") && Field("backup") is null, "Page accepted an oversized file");
// 매우 큰 스트림은 제한을 넘는 즉시 읽기를 멈춰야 합니다(메모리 보호).
ResetPage();
var endless = new CountingZeroStream(200L * 1024 * 1024);
await Call("LoadAsync", endless, "endless.json");
Check(endless.Served <= BackupLimits.MaxFileBytes + 2 * 81_920 && PageIssues().Count == 1, $"Page kept reading an oversized stream ({endless.Served:N0} bytes)");
ResetPage();
await Call("LoadAsync", new MemoryStream(bytesA), "backup.json");
var pagePreview = (BackupRestoreReport)Field("preview")!;
Check(PageIssues().Count == 0 && Field("backup") is not null && pagePreview is { Mode: BackupRestoreMode.Merge, DryRun: true } && pagePreview.TotalAdded > 0 && CanApply(), "Page did not build a merge preview");
Check((await Export("P")).Counts.Total == 0, "Preview changed the database");

await Call("ChangeModeAsync", BackupRestoreMode.Replace);
Check(((BackupRestoreReport)Field("preview")!).Mode == BackupRestoreMode.Replace && !CanApply(), "Replace must require the confirmation phrase");
await Call("ApplyAsync");
Check((string?)Field("errorMessage") is { Length: > 0 } && Field("result") is null && (await Export("P")).Counts.Total == 0, "Replace ran without the confirmation phrase");
SetField("confirmText", "덮어쓰기 "); Check(CanApply(), "Phrase with surrounding spaces should be accepted");
SetField("confirmText", "덮어쓰"); Check(!CanApply(), "Partial phrase should not be accepted");
SetField("confirmText", "덮어쓰기");

// 확인 문구까지 입력했더라도 로그인 계정이 바뀌었다면 저장하지 않습니다.
pageAuth.OwnerId = "Q";
await Call("ApplyAsync");
Check((string?)Field("errorMessage") is { Length: > 0 } && (await Export("P")).Counts.Total == 0 && (await Export("Q")).Counts.Total == 0, "Identity change not blocked");
pageAuth.OwnerId = "P";

SetField("busy", false);
await Call("ApplyAsync");
var pageResult = (BackupRestoreReport)Field("result")!;
Check(pageResult.Mode == BackupRestoreMode.Replace && !pageResult.DryRun && Field("backup") is null && Field("preview") is null && (string?)Field("errorMessage") is null, "Page apply failed");
Check((await ExportBytes("P")).SequenceEqual(bytesA), "Page restore did not reproduce the backup");

// 병합 흐름: 같은 파일을 다시 선택해 병합하면 추가되는 것이 없다
ResetPage();
await Call("LoadAsync", new MemoryStream(bytesA), "backup.json");
Check(((BackupRestoreReport)Field("preview")!).TotalAdded == 0, "Merging the same backup again should add nothing");
await Call("ApplyAsync");
Check(((BackupRestoreReport)Field("result")!).TotalAdded == 0 && (await ExportBytes("P")).SequenceEqual(bytesA), "Repeated merge changed data");

// ---- 13. 계정 삭제는 복원된 데이터도 모두 지운다(다른 계정은 유지) ----
await new UserDataDeletionService(factory).DeleteAsync("C");
Check((await Export("C")).Counts.Total == 0 && (await ExportBytes("A")).SequenceEqual(bytesA), "Account deletion left backup-restored data or affected another account");

Console.WriteLine("PASS: export, byte-exact round trip across accounts, merge idempotency with legitimate duplicates, partial-loss refill, dry-run parity, replace, payment-method mapping, recurring history, atomic rollback, endpoint headers");

sealed class FailOnNthSave(int failOn) : SaveChangesInterceptor
{
    public int Calls { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (++Calls == failOn) throw new InvalidOperationException("injected");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

// 지정한 길이만큼 0을 돌려주며 지금까지 읽힌 바이트 수를 셉니다.
sealed class CountingZeroStream(long length) : Stream
{
    public long Served { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => Served; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => Fill(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => Fill(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Fill(buffer.Span));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Fill(buffer.AsSpan(offset, count)));
    private int Fill(Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, length - Served);
        buffer[..count].Clear();
        Served += count;
        return count;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
