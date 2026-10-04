using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

static ExpenseRecord Exp(int id, string date, long amount, string category, string memo, int? method = null, string owner = "A") =>
    new() { Id = id, OwnerId = owner, Date = DateTime.Parse(date), Amount = amount, Category = category, Memo = memo, PaymentMethodId = method };
static IncomeRecord Inc(int id, string date, long amount, string source = "급여") =>
    new() { Id = id, OwnerId = "A", Date = DateTime.Parse(date), Amount = amount, Source = source, Memo = "" };

var card = new PaymentMethod { Id = 1, OwnerId = "A", Name = "카드", Type = "신용카드" };
var cash = new PaymentMethod { Id = 2, OwnerId = "A", Name = "현금", Type = "현금" };
var methods = new Dictionary<int, PaymentMethod> { [1] = card, [2] = cash };

var expenses = new List<ExpenseRecord>
{
    Exp(1, "2026-01-05", 10_000, "식비", "Coffee", 1),
    Exp(2, "2026-01-06", 4_500, "식비", "coffee ", 1),
    Exp(3, "2026-01-31", 2_000, "교통", ""),
    Exp(4, "2026-03-10", 300_000, "쇼핑", "노트북", 2),
    Exp(5, "2026-09-30", 20_000, "식비", "coffee", 1),
    Exp(6, "2026-10-15 23:59:59", 5_000, "식비", "점심"),
    Exp(7, "2026-10-16", 99_999, "식비", "내일"),          // 미래: 제외
    Exp(8, "2025-01-05", 8_000, "식비", "작년"),
    Exp(9, "2025-10-15", 1_000, "식비", "작년 같은 날"),    // 비교 기간에 포함
    Exp(10, "2025-10-16", 7_777, "교통", "작년 다음 날"),   // 비교 기간 밖
    Exp(11, "2025-12-31", 50_000, "식비", "작년 말"),       // 비교 기간 밖
    Exp(12, "2025-03-01", 3_000, "여가", "작년에만")
};
var incomes = new List<IncomeRecord>
{
    Inc(1, "2026-01-25", 1_000_000), Inc(2, "2026-02-25", 1_000_000), Inc(3, "2026-10-10", 200_000, "부수입"),
    Inc(4, "2025-01-25", 900_000), Inc(5, "2025-11-25", 800_000)
};
var today = new DateOnly(2026, 10, 15);
var report = YearlyStatistics.Calculate(expenses, incomes, methods, 2026, today);

Check(report.IsCurrentYear && report.From == new DateOnly(2026, 1, 1) && report.To == today, "Current period wrong");
Check(report.PreviousFrom == new DateOnly(2025, 1, 1) && report.PreviousTo == new DateOnly(2025, 10, 15), "Previous period wrong");
Check(report.Current == new YearTotals(2_200_000, 341_500, 3, 6), $"Current totals wrong: {report.Current}");
Check(report.Previous == new YearTotals(900_000, 12_000, 1, 3), $"Previous totals wrong: {report.Previous}");
Check(report.Current.Net == 1_858_500 && report.Current.SavingsRate == 84.5, "Net or savings rate wrong");
Check(report.AverageMonthlyExpense == 37_388 && report.AverageMonthCount == 9, $"Average wrong: {report.AverageMonthlyExpense}");

Check(report.Months.Count == 12, "Month count wrong");
Check(report.Months[0].Income == 1_000_000 && report.Months[0].Expense == 16_500 && report.Months[0].ExpenseCount == 3, "January wrong");
Check(report.Months[0].SavingsRate == 98.4, $"January rate wrong: {report.Months[0].SavingsRate}");
Check(report.Months[3].Income == 0 && report.Months[3].SavingsRate is null, "No-income month must have no rate");
Check(report.Months[9].IsPartial && report.Months[9].Expense == 5_000 && report.Months[9].Income == 200_000, "Partial month wrong");
Check(report.Months[10].IsFuture && report.Months[11].IsFuture && !report.Months[9].IsFuture, "Future flags wrong");
Check(report.Months.Sum(m => m.Expense) == report.Current.Expense && report.Months.Sum(m => m.Income) == report.Current.Income, "Months do not add up");

Check(report.Categories.Select(c => c.Category).SequenceEqual(new[] { "쇼핑", "식비", "교통", "여가" }), "Category order wrong: " + string.Join(",", report.Categories.Select(c => c.Category)));
var food = report.Categories[1];
Check(food.Amount == 39_500 && food.Count == 4 && food.PreviousAmount == 9_000 && food.Difference == 30_500 && food.PercentageChange == 338.9, "Food row wrong");
Check(Math.Abs(food.Percentage - 39_500.0 / 341_500 * 100) < 1e-9, "Food share wrong");
Check(report.Categories[0].PreviousAmount == 0 && report.Categories[0].PercentageChange is null, "New category must have no rate");
Check(report.Categories[3].Amount == 0 && report.Categories[3].PreviousAmount == 3_000 && report.Categories[3].PercentageChange == -100.0, "Previous-only category wrong");
Check(Math.Abs(report.Categories.Sum(c => c.Percentage) - 100) < 1e-9, "Category shares must total 100");

Check(report.Methods.Select(m => (m.Name, m.Amount, m.Count)).SequenceEqual(new[] { ("현금", 300_000L, 1), ("카드", 34_500L, 3), ("미지정", 7_000L, 2) }), "Method rows wrong");
Check(report.Methods[1].Type == "신용카드" && report.Methods[2].Type == "", "Method types wrong");

Check(report.TopExpenses.Select(t => t.Amount).SequenceEqual(new long[] { 300_000, 20_000, 10_000, 5_000, 4_500, 2_000 }), "Top expenses wrong");
Check(report.TopExpenses[0].Memo == "노트북" && report.TopExpenses[0].Date == new DateOnly(2026, 3, 10), "Top expense fields wrong");

Check(report.FrequentMemos.Count == 1 && report.FrequentMemos[0] == new FrequentMemoRow("Coffee", 3, 34_500), $"Frequent memos wrong: {string.Join(";", report.FrequentMemos)}");

Check(report.Weekdays.Select(w => w.Day).SequenceEqual(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }), "Weekday order wrong");
Check(report.Weekdays.Select(w => w.Amount).SequenceEqual(new long[] { 10_000, 304_500, 20_000, 5_000, 0, 2_000, 0 }), "Weekday amounts wrong");
Check(report.Weekdays.Sum(w => w.Count) == 6, "Weekday counts wrong");

// 10건을 넘으면 상위 10건만 남기고, 같은 금액은 최근 날짜가 먼저입니다.
var many = Enumerable.Range(1, 15).Select(i => Exp(100 + i, $"2026-02-{i:00}", i == 3 || i == 9 ? 777 : 1_000 + i, "식비", $"항목{i % 3}")).ToList();
var manyReport = YearlyStatistics.Calculate(many, [], methods, 2026, today);
Check(manyReport.TopExpenses.Count == 10, "Top limit wrong");
Check(manyReport.FrequentMemos.Count == 3 && manyReport.FrequentMemos.All(m => m.Count == 5), "Frequent memo limit wrong");
var ties = YearlyStatistics.Calculate([Exp(1, "2026-02-01", 500, "식비", "a"), Exp(2, "2026-02-09", 500, "식비", "b"), Exp(3, "2026-02-09", 500, "식비", "c")], [], methods, 2026, today);
Check(ties.TopExpenses.Select(t => t.Memo).SequenceEqual(new[] { "c", "b", "a" }), "Tie order wrong");

// 지난 해: 한 해 전체를 집계하고 미래·진행 중인 달이 없습니다.
var past = YearlyStatistics.Calculate(expenses, incomes, methods, 2025, today);
Check(!past.IsCurrentYear && past.To == new DateOnly(2025, 12, 31) && past.PreviousTo == new DateOnly(2024, 12, 31), "Past year period wrong");
Check(past.Current.Expense == 8_000 + 1_000 + 7_777 + 50_000 + 3_000 && past.Current.Income == 1_700_000, "Past year totals wrong");
Check(past.Months.All(m => !m.IsFuture && !m.IsPartial) && past.AverageMonthCount == 12 && past.AverageMonthlyExpense == 69_777 / 12, "Past year average wrong");
Check(past.Previous.Expense == 0 && past.Previous.Income == 0, "Past year previous wrong");

// 윤일: 2028-02-29의 비교 기간은 전년도 2월 28일까지입니다.
var leap = YearlyStatistics.Calculate([Exp(1, "2027-02-28", 1, "식비", ""), Exp(2, "2027-03-01", 2, "식비", "")], [], methods, 2028, new DateOnly(2028, 2, 29));
Check(leap.PreviousTo == new DateOnly(2027, 2, 28) && leap.Previous.Expense == 1, "Leap day comparison wrong");

// 1월 초에는 끝난 달이 없어 월 평균이 없습니다. 기록이 없어도 계산은 성공합니다.
var january = YearlyStatistics.Calculate([], [], methods, 2026, new DateOnly(2026, 1, 3));
Check(january.AverageMonthlyExpense is null && january.AverageMonthCount == 0 && january.Current == new YearTotals(0, 0, 0, 0), "Empty January wrong");
Check(january.Categories.Count == 0 && january.Methods.Count == 0 && january.TopExpenses.Count == 0 && january.Weekdays.All(w => w.Percentage == 0), "Empty lists wrong");

// 삭제된 결제수단 번호는 미지정으로 묶습니다.
var orphan = YearlyStatistics.Calculate([Exp(1, "2026-02-01", 100, "식비", "", 99)], [], methods, 2026, today);
Check(orphan.Methods.Count == 1 && orphan.Methods[0].Name == "미지정", "Orphan payment method wrong");

foreach (var bad in new[] { 1, 2027 })
{
    try { YearlyStatistics.Calculate([], [], methods, bad, today); throw new Exception("Bad year accepted"); }
    catch (ArgumentOutOfRangeException) { }
}

// 서비스: 다른 계정 제외, 오늘 23:59 포함, 연도 목록.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using (var setup = new ExpensesDbContext(options)) await setup.Database.EnsureCreatedAsync();
await using (var db = new ExpensesDbContext(options))
{
    db.PaymentMethods.AddRange(new PaymentMethod { OwnerId = "A", Name = "카드", Type = "신용카드" }, new PaymentMethod { OwnerId = "B", Name = "남의카드", Type = "신용카드" });
    await db.SaveChangesAsync();
    var cardId = db.PaymentMethods.Single(m => m.OwnerId == "A").Id;
    foreach (var e in expenses) { e.Id = 0; e.PaymentMethodId = e.PaymentMethodId == 1 ? cardId : null; db.Expenses.Add(e); }
    foreach (var i in incomes) { i.Id = 0; db.Incomes.Add(i); }
    db.Expenses.Add(Exp(0, "2026-02-02", 9_999_999, "식비", "남", owner: "B"));
    db.Incomes.Add(new IncomeRecord { OwnerId = "B", Date = new DateTime(2026, 2, 2), Amount = 8_888_888, Source = "급여", Memo = "" });
    await db.SaveChangesAsync();
}
var factory = new TestFactory(options);
var service = new YearlyStatisticsService(factory);
var loaded = await service.LoadAsync("A", 2026, today);
Check(loaded.Current == new YearTotals(2_200_000, 341_500, 3, 6) && loaded.Previous == new YearTotals(900_000, 12_000, 1, 3), "Service totals wrong (owner leak or range)");
Check(loaded.Methods.Any(m => m.Name == "카드" && m.Amount == 34_500) && loaded.Methods.All(m => m.Name != "남의카드"), "Service methods wrong");
Check((await service.LoadAsync("B", 2026, today)).Current.Expense == 9_999_999, "Owner B totals wrong");
Check((await service.LoadAsync("Z", 2026, today)).Current == new YearTotals(0, 0, 0, 0), "Empty owner wrong");
Check((await service.AvailableYearsAsync("A", today)).SequenceEqual(new[] { 2026, 2025 }), "Years wrong");
Check((await service.AvailableYearsAsync("Z", today)).SequenceEqual(new[] { 2026 }), "Years for empty owner wrong");
await using (var db = new ExpensesDbContext(options))
{
    db.Incomes.Add(new IncomeRecord { OwnerId = "Y", Date = new DateTime(2024, 5, 1), Amount = 1, Source = "급여", Memo = "" });
    db.Expenses.Add(Exp(0, "0001-06-01", 1, "식비", "", owner: "Y"));
    await db.SaveChangesAsync();
}
var yearsY = await service.AvailableYearsAsync("Y", today);
Check(yearsY[0] == 2026 && yearsY[^1] == YearlyStatistics.MinimumYear && yearsY.Count == 2026 - 2 + 1, "Year clamp wrong");
foreach (var bad in new[] { 1, 2027 })
{
    try { await service.LoadAsync("A", bad, today); throw new Exception("Service accepted bad year"); }
    catch (ArgumentOutOfRangeException) { }
}
try { await service.LoadAsync(" ", 2026, today); throw new Exception("Blank owner accepted"); } catch (ArgumentException) { }

// 페이지: 초기 로드, 연도 변경, 잘못된 연도, 계정 변경.
var realToday = DateOnly.FromDateTime(KoreanClock.Today);
await using (var db = new ExpensesDbContext(options))
{
    db.Expenses.Add(Exp(0, $"{realToday.Year}-01-01", 1_234, "식비", "올해", owner: "P"));
    db.Expenses.Add(Exp(0, $"{realToday.Year - 1}-03-01", 4_321, "식비", "작년", owner: "P"));
    await db.SaveChangesAsync();
}
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var auth = new TestAuth("P");
var page = new Statistics();
void Inject(string name, object value) => typeof(Statistics).GetProperty(name, flags)!.SetValue(page, value);
Inject("AuthenticationStateProvider", auth);
Inject("StatisticsService", new YearlyStatisticsService(factory));
Inject("RecurringExpenseService", new RecurringExpenseService(factory));
Inject("RecurringIncomeService", new RecurringIncomeService(factory));
Inject("Logger", NullLogger<Statistics>.Instance);
T Field<T>(string name) => (T)typeof(Statistics).GetField(name, flags)!.GetValue(page)!;
Task Call(string method, params object[] args) => (Task)typeof(Statistics).GetMethod(method, flags)!.Invoke(page, args)!;

await Call("OnInitializedAsync");
var pageReport = Field<YearlyReport?>("report");
Check(pageReport is not null && pageReport.Year == realToday.Year && pageReport.Current.Expense == 1_234, "Page initial load wrong");
Check(Field<IReadOnlyList<int>>("years").SequenceEqual(new[] { realToday.Year, realToday.Year - 1 }), "Page years wrong");
await Call("OnYearChangedAsync", new ChangeEventArgs { Value = (realToday.Year - 1).ToString() });
Check(Field<YearlyReport?>("report")!.Year == realToday.Year - 1 && Field<YearlyReport?>("report")!.Current.Expense == 4_321, "Page year change wrong");
foreach (var bad in new[] { "1999", "abc", "", "-1" })
{
    await Call("OnYearChangedAsync", new ChangeEventArgs { Value = bad });
    Check(Field<string?>("errorMessage") is not null && Field<YearlyReport?>("report")!.Year == realToday.Year - 1, $"Page accepted year '{bad}'");
}
auth.OwnerId = "Q";
await Call("ReloadAsync");
Check(Field<YearlyReport?>("report") is null && Field<string?>("errorMessage")!.Contains("로그인"), "Page did not block account change");

Console.WriteLine("PASS: yearly statistics, comparisons, ranking, service isolation and page logic");
