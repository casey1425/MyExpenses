using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;
using MyExpenses.Testing;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using (var setup = new ExpensesDbContext(options)) await setup.Database.EnsureCreatedAsync();
var factory = new TestFactory(options);
var service = new ExpenseService(factory);
var today = KoreanClock.Today;

// ── 메모 자동 완성 ──
var card = new PaymentMethod { OwnerId = "A", Name = "신한카드", Type = "신용카드" };
await using (var db = new ExpensesDbContext(options))
{
    db.PaymentMethods.Add(card);
    await db.SaveChangesAsync();
    ExpenseRecord E(string who, int daysAgo, long amount, string category, string memo, int? method = null) =>
        new() { OwnerId = who, Date = today.AddDays(-daysAgo), Amount = amount, Category = category, Memo = memo, PaymentMethodId = method };
    db.Expenses.AddRange(
        E("A", 30, 4_000, "식비", "스타벅스"), E("A", 20, 4_500, "카페", "스타벅스", card.Id), E("A", 5, 5_000, "카페", " 스타벅스 ", card.Id),
        E("A", 9, 9_000, "식비", "점심"), E("A", 9, 9_500, "식비", "점심"),
        E("A", 6, 3_000, "카페", "Latte"), E("A", 4, 3_100, "카페", "latte"),   // 대소문자만 다른 같은 메모
        E("A", 3, 1_250, "교통", "버스"), E("A", 10, 1_300, "교통", "버스"),   // 나중에 넣었지만 더 오래된 기록
        E("A", 2, 700, "기타", ""),                        // 메모 없음: 제외
        E("A", 400, 1_000, "식비", "작년메모"),             // 1년보다 오래됨: 제외
        E("A", -3, 8_888, "식비", "미래메모"),              // 미래: 제외
        E("B", 1, 77_000, "식비", "남의메모"));
    await db.SaveChangesAsync();
}
var suggestions = await service.SuggestionsAsync("A", today);
Check(suggestions.Select(s => s.Memo).SequenceEqual(new[] { "스타벅스", "latte", "버스", "점심" }), "Suggestion order wrong: " + string.Join(",", suggestions.Select(s => s.Memo)));
var latte = suggestions[0];
Check(latte.Count == 3 && latte.Category == "카페" && latte.Amount == 5_000 && latte.PaymentMethodId == card.Id, "Latest values not used for suggestion");
Check(suggestions[1].Count == 2 && suggestions[1].Amount == 3_100 && suggestions[1].Memo == "latte", "Memos differing only in case must merge, newest wins");
Check(suggestions[2].Count == 2 && suggestions[2].Amount == 1_250, "Newest date must win over a newer id: " + suggestions[2].Amount);
Check(suggestions[3].Count == 2 && suggestions[3].Amount == 9_500, "Tie on the same date must use the latest id: " + suggestions[3].Amount);
Check((await service.SuggestionsAsync("B", today)).Single().Memo == "남의메모", "Owner isolation failed");
Check((await service.SuggestionsAsync("Z", today)).Count == 0, "Empty owner suggestions");
await using (var db = new ExpensesDbContext(options))
{
    for (var i = 0; i < 40; i++) db.Expenses.Add(new() { OwnerId = "C", Date = today.AddDays(-1), Amount = 100 + i, Category = "식비", Memo = $"메모{i:00}" });
    await db.SaveChangesAsync();
}
Check((await service.SuggestionsAsync("C", today)).Count == ExpenseService.SuggestionLimit, "Suggestion limit wrong");

// ── 달력 계산 ──
static ExpenseRecord Ex(string date, long amount) => new() { OwnerId = "A", Date = DateTime.Parse(date), Amount = amount, Category = "식비", Memo = "" };
static IncomeRecord In(string date, long amount) => new() { OwnerId = "A", Date = DateTime.Parse(date), Amount = amount, Source = "급여", Memo = "" };
var view = CalendarMonth.Calculate(
    [Ex("2026-10-01", 1_000), Ex("2026-10-01 18:30", 2_500), Ex("2026-10-31 23:59:59", 700), Ex("2026-09-30", 99_999), Ex("2026-11-01", 88_888)],
    [In("2026-10-25", 2_000_000), In("2026-10-25", 50_000), In("2026-11-01", 5)],
    new DateOnly(2026, 10, 17), new DateOnly(2026, 10, 15));
// 2026-10-01은 목요일, 10-31은 토요일 → 일요일 시작 5주
Check(view.Weeks.Count == 5 && view.Weeks.All(w => w.Count == 7), "Week layout wrong");
Check(view.Weeks[0].Take(4).All(d => !d.InMonth) && view.Weeks[0][4].Date == new DateOnly(2026, 10, 1) && view.Weeks[0][4].InMonth, "First week padding wrong");
Check(view.Weeks[4][6].Date == new DateOnly(2026, 10, 31) && view.Weeks[4][6].InMonth, "Last day placement wrong");
Check(view.Weeks[0].All(d => d.Date.DayOfWeek == (DayOfWeek)Array.IndexOf(view.Weeks[0].ToArray(), d)), "Sunday-first order wrong");
var first = view.Weeks[0][4];
Check(first.Expense == 3_500 && first.ExpenseCount == 2 && first.Income == 0, "Day totals wrong");
Check(view.Weeks[4][6].Expense == 700, "Month-end 23:59:59 expense missing");
Check(view.Weeks.SelectMany(w => w).Where(d => !d.InMonth).All(d => d.Expense == 0 && d.Income == 0 && !d.HasRecords), "Padding days must be empty");
var payday = view.Weeks.SelectMany(w => w).Single(d => d.Date == new DateOnly(2026, 10, 25));
Check(payday.Income == 2_050_000 && payday.IncomeCount == 2, "Income day wrong");
Check(view.ExpenseTotal == 4_200 && view.ExpenseCount == 3 && view.IncomeTotal == 2_050_000 && view.IncomeCount == 2 && view.Net == 2_045_800, "Month totals wrong (adjacent months leaked?)");
Check(view.Weeks.SelectMany(w => w).Single(d => d.IsToday).Date == new DateOnly(2026, 10, 15), "Today flag wrong");
// 달력 모양: 일요일에 시작하는 달·토요일에 끝나는 달·6주 달·윤년 2월
Check(CalendarMonth.Calculate([], [], new DateOnly(2026, 2, 1), today: new DateOnly(2026, 2, 1)).Weeks.Count == 4, "Feb 2026 (Sun-Sat) should be 4 weeks");
Check(CalendarMonth.Calculate([], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1)).Weeks.Count == 6, "Aug 2026 should need 6 weeks");
var leap = CalendarMonth.Calculate([Ex("2028-02-29", 5)], [], new DateOnly(2028, 2, 10), new DateOnly(2028, 2, 10));
Check(leap.Weeks.SelectMany(w => w).Count(d => d.InMonth) == 29 && leap.ExpenseTotal == 5, "Leap February wrong");
for (var m = 1; m <= 12; m++)
{
    var v = CalendarMonth.Calculate([], [], new DateOnly(2026, m, 1), DateOnly.FromDateTime(today));
    Check(v.Weeks.SelectMany(w => w).Count(d => d.InMonth) == DateTime.DaysInMonth(2026, m) && v.Weeks[0].Any(d => d.InMonth) && v.Weeks[^1].Any(d => d.InMonth), $"Month {m} layout wrong");
}
foreach (var bad in new[] { new DateOnly(1, 12, 1), new DateOnly(9999, 1, 1) })
{
    try { CalendarMonth.Calculate([], [], bad, today: new DateOnly(2026, 1, 1)); throw new Exception("Bad month accepted"); }
    catch (ArgumentOutOfRangeException) { }
}
Check(CalendarMonth.Calculate([], [], new DateOnly(2, 1, 1), new DateOnly(2026, 1, 1)).Weeks.Count >= 4 && CalendarMonth.Calculate([], [], new DateOnly(9998, 12, 1), new DateOnly(2026, 1, 1)).Weeks.Count >= 4, "Boundary months failed");
foreach (var (value, text) in new (long, string)[] { (0, ""), (-5, ""), (1, "1"), (9_999, "9,999"), (10_000, "1만"), (12_000, "1.2만"), (99_999, "10만"), (123_456, "12.3만"), (1_500_000, "150만"), (99_990_000, "9999만"), (100_000_000, "1억"), (250_000_000, "2.5억") })
    Check(CalendarMonth.Compact(value) == text, $"Compact({value}) = '{CalendarMonth.Compact(value)}', expected '{text}'");

// ── 달력 서비스 ──
await using (var db = new ExpensesDbContext(options))
{
    foreach (var e in new[] { Ex("2026-10-01", 111), Ex("2026-10-31 23:59:59", 222), Ex("2026-11-01", 333), Ex("2026-09-30 23:59:59", 444) }) { e.OwnerId = "K"; db.Expenses.Add(e); }
    db.Expenses.Add(new() { OwnerId = "B", Date = new DateTime(2026, 10, 5), Amount = 9_999, Category = "식비", Memo = "남" });
    var kIncome = In("2026-10-10", 1_000); kIncome.OwnerId = "K";
    db.Incomes.AddRange(kIncome, new IncomeRecord { OwnerId = "B", Date = new DateTime(2026, 10, 10), Amount = 5, Source = "급여", Memo = "" });
    await db.SaveChangesAsync();
}
var calendar = new CalendarService(factory);
var loaded = await calendar.LoadAsync("K", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 15));
Check(loaded.Expenses.Select(e => e.Amount).SequenceEqual(new long[] { 111, 222 }) && loaded.Incomes.Single().Amount == 1_000, "Calendar service range or owner filter wrong");
Check(loaded.View.ExpenseTotal == 333 && loaded.View.IncomeTotal == 1_000, "Calendar service totals wrong");
foreach (var bad in new[] { new DateOnly(1, 1, 1), new DateOnly(9999, 1, 1) })
{
    try { await calendar.LoadAsync("K", bad, today: new DateOnly(2026, 10, 15)); throw new Exception("Service accepted bad month"); }
    catch (ArgumentOutOfRangeException) { }
}
try { await calendar.LoadAsync(" ", new DateOnly(2026, 10, 1), today: new DateOnly(2026, 10, 15)); throw new Exception("Blank owner accepted"); } catch (ArgumentException) { }

// ── 지출 화면: 연속 입력·자동 완성·월 이동·되돌리기 ──
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
await using var homeDb = new ExpensesDbContext(options);
var auth = new TestAuth("H");
var home = TestComponents.CreateHome(factory, auth);
void Set(string name, object? value) => typeof(Home).GetField(name, flags)!.SetValue(home, value);
T Get<T>(string name) => (T)typeof(Home).GetField(name, flags)!.GetValue(home)!;
Task Call(string method, params object[] args) => (Task)typeof(Home).GetMethod(method, flags)!.Invoke(home, args)!;
object? CallSync(string method, params object[] args) => typeof(Home).GetMethod(method, flags)!.Invoke(home, args);
var method1 = new PaymentMethod { OwnerId = "H", Name = "국민카드", Type = "신용카드" };
homeDb.PaymentMethods.Add(method1);
homeDb.Expenses.AddRange(
    new ExpenseRecord { OwnerId = "H", Date = today.AddDays(-4), Amount = 4_500, Category = "카페", Memo = "스타벅스", PaymentMethodId = null },
    new ExpenseRecord { OwnerId = "H", Date = today.AddDays(-2), Amount = 5_200, Category = "카페", Memo = "스타벅스" });
await homeDb.SaveChangesAsync();
// 최근 기록의 결제수단을 가진 메모를 하나 더 만들어 자동 완성이 결제수단도 채우는지 확인합니다.
homeDb.Expenses.Add(new ExpenseRecord { OwnerId = "H", Date = today.AddDays(-1), Amount = 12_000, Category = "식비", Memo = "회식", PaymentMethodId = method1.Id });
await homeDb.SaveChangesAsync();
await Call("LoadCategoriesAsync");
await Call("LoadPaymentMethodsAsync");
await Call("LoadExpensesAsync");
await Call("LoadSuggestionsAsync");
Check(Get<List<MemoSuggestion>>("memoSuggestions").Select(s => s.Memo).SequenceEqual(new[] { "스타벅스", "회식" }), "Home suggestions not loaded");

// 자동 완성: 알려진 메모는 카테고리·결제수단·금액을 채우고, 이미 입력한 금액은 지키며, 모르는 메모는 건드리지 않음
Set("category", "식비"); Set("amount", 0L);
CallSync("OnMemoChanged", "회식");
Check(Get<string>("category") == "식비" && Get<int?>("paymentMethodId") == method1.Id && Get<long>("amount") == 12_000 && Get<string?>("autofillNotice") is { Length: > 0 }, "Autofill (payment+amount) failed");
Set("amount", 777L); Set("category", "교통"); Set("paymentMethodId", null);
CallSync("OnMemoChanged", " 스타벅스 ");
Check(Get<string>("category") == "카페" && Get<long>("amount") == 777 && Get<int?>("paymentMethodId") is null, "Autofill must keep a typed amount and ignore null payment");
Set("category", "교통");
CallSync("OnMemoChanged", "처음 보는 메모");
Check(Get<string>("category") == "교통" && Get<string?>("autofillNotice") is null && Get<string>("memo") == "처음 보는 메모", "Unknown memo must not change the form");
Set("category", "교통");
CallSync("OnMemoChanged", "스타벅스");
CallSync("OnMemoChanged", "스타벅스");
Check(Get<string?>("autofillNotice") is null, "Second identical autofill must not announce again");

// 연속 입력: 날짜·카테고리·결제수단은 유지하고 금액·메모만 비움
Set("expenseDate", today.AddDays(-1)); Set("amount", 3_300L); Set("category", "카페"); Set("memo", "아메리카노"); Set("paymentMethodId", method1.Id);
await Call("AddExpenseAsync");
Check(Get<long>("amount") == 0 && Get<string>("memo") == "", "Amount/memo not cleared after add");
Check(Get<DateTime>("expenseDate") == today.AddDays(-1) && Get<string>("category") == "카페" && Get<int?>("paymentMethodId") == method1.Id, "Date/category/payment must be kept after add");
Check(Get<List<MemoSuggestion>>("memoSuggestions").Any(s => s.Memo == "아메리카노"), "New memo not offered after add");
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H" && e.Memo == "아메리카노" && e.Amount == 3_300 && e.PaymentMethodId == method1.Id) == 1, "Added expense wrong");

// 월 이동
await Call("ShiftMonthAsync", -1);
var expectedPrev = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
Check(Get<ExpenseFilter>("activeFilter").Month == expectedPrev && Get<ExpenseSearchInput>("searchInput").Month == expectedPrev.ToString("yyyy-MM"), "Shift back wrong");
await Call("ShiftMonthAsync", 2);
Check(Get<ExpenseFilter>("activeFilter").Month == expectedPrev.AddMonths(2), "Shift forward wrong");
await Call("ShowThisMonthAsync");
Check(Get<ExpenseFilter>("activeFilter").Month == new DateTime(today.Year, today.Month, 1), "This month wrong");
Check(typeof(Home).GetProperty("HasDetailedFilter", flags)!.GetValue(home) is false, "Month-only filter must not count as a detailed filter");
Set("searchInput", new ExpenseSearchInput { Month = "2026-10", Search = "스타" });
await Call("ApplyFiltersAsync");
Check(typeof(Home).GetProperty("HasDetailedFilter", flags)!.GetValue(home) is true, "Search filter must count as detailed");
await Call("ResetFiltersAsync");
Check(Get<ExpenseFilter>("activeFilter").Month is null && typeof(Home).GetProperty("MonthNavLabel", flags)!.GetValue(home) is "전체 기간", "Reset label wrong");
await Call("ShiftMonthAsync", -1);   // 필터가 없으면 이번 달 기준
Check(Get<ExpenseFilter>("activeFilter").Month == expectedPrev, "Shift without a filter must start from this month");
Set("searchInput", new ExpenseSearchInput { Month = "0001-12" });
Set("activeFilter", new ExpenseFilter(Month: new DateTime(2, 1, 1)));
await Call("ShiftMonthAsync", -1);
Check(Get<ExpenseFilter>("activeFilter").Month == new DateTime(2, 1, 1), "Shift below the minimum year must be ignored");
await Call("ResetFiltersAsync");

// 삭제 되돌리기
await Call("LoadExpensesAsync");
var target = Get<List<ExpenseRecord>>("expenses").First(e => e.Memo == "회식");
var before = await homeDb.Expenses.CountAsync(e => e.OwnerId == "H");
await Call("DeleteExpenseAsync", target.Id);
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H") == before - 1 && Get<ExpenseInput?>("lastDeleted") is not null, "Delete did not offer undo");
await Call("UndoDeleteAsync");
var restored = await homeDb.Expenses.AsNoTracking().SingleAsync(e => e.OwnerId == "H" && e.Memo == "회식");
Check(restored.Amount == 12_000 && restored.Category == "식비" && restored.Date == target.Date && restored.PaymentMethodId == method1.Id, "Undo restored different data");
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H") == before && Get<ExpenseInput?>("lastDeleted") is null && Get<string?>("historyNotice") is { Length: > 0 }, "Undo bookkeeping wrong");
await Call("UndoDeleteAsync");
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H") == before, "Second undo must do nothing");
// 되돌리기는 다른 작업을 하면 사라집니다.
target = Get<List<ExpenseRecord>>("expenses").First(e => e.Memo == "회식");
await Call("DeleteExpenseAsync", target.Id);
Check(Get<ExpenseInput?>("lastDeleted") is not null, "Undo not offered again");
Set("searchInput", new ExpenseSearchInput());
await Call("ApplyFiltersAsync");
Check(Get<ExpenseInput?>("lastDeleted") is null, "Undo must be dropped by a new search");
// 그 사이 결제수단이 사라졌다면 안전하게 거절합니다(데이터를 만들지 않음).
await Call("LoadExpensesAsync");
target = Get<List<ExpenseRecord>>("expenses").First(e => e.Memo == "아메리카노");
await Call("DeleteExpenseAsync", target.Id);
var withMethod = await homeDb.Expenses.CountAsync(e => e.OwnerId == "H");
await homeDb.PaymentMethods.Where(m => m.Id == method1.Id).ExecuteDeleteAsync();
await Call("UndoDeleteAsync");
// 결제수단 삭제는 연결을 끊을 뿐이라 기록은 '미지정'으로 되돌아오지 않고 검증에서 거절됩니다.
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H") == withMethod && Get<string?>("deleteError") is { Length: > 0 } && Get<ExpenseInput?>("lastDeleted") is null, "Undo with a deleted payment method must be rejected cleanly");
// 보관한 카테고리도 마찬가지
await Call("LoadExpensesAsync");
target = Get<List<ExpenseRecord>>("expenses").First(e => e.Memo == "스타벅스");
await Call("DeleteExpenseAsync", target.Id);
await homeDb.UserCategories.Where(c => c.OwnerId == "H" && c.Name == "카페").ExecuteUpdateAsync(s => s.SetProperty(c => c.IsArchived, true));
var afterArchive = await homeDb.Expenses.CountAsync(e => e.OwnerId == "H");
await Call("UndoDeleteAsync");
Check(await homeDb.Expenses.CountAsync(e => e.OwnerId == "H") == afterArchive && Get<string?>("deleteError") is { Length: > 0 }, "Undo into an archived category must be rejected");
// 전체 삭제는 되돌리기를 제공하지 않음
Set("lastDeleted", new ExpenseInput(today, 1, "식비", ""));
Set("showDeleteAllConfirmation", true);
await Call("DeleteAllExpensesAsync");
Check(Get<ExpenseInput?>("lastDeleted") is null, "Delete all must clear undo");
// 계정이 바뀐 상태에서는 되돌리기 거부
Set("lastDeleted", new ExpenseInput(today, 1, "식비", "x"));
auth.OwnerId = "OTHER";
await Call("UndoDeleteAsync");
Check(await homeDb.Expenses.CountAsync(e => e.Memo == "x") == 0, "Undo ran for a different account");
auth.OwnerId = "H";

// 쿼리의 date 값: 올바른 날짜만 입력 날짜로 쓰고, 이상한 값은 무시
await using (var db = new ExpensesDbContext(options))
{
    db.UserProfiles.Add(new UserProfile { OwnerId = "Q", HasCompletedOnboarding = true });
    await db.SaveChangesAsync();
}
foreach (var (raw, expected) in new (string?, DateTime?)[] { ("2026-03-07", new DateTime(2026, 3, 7)), ("2026-02-30", null), ("abc", null), (null, null), ("0001-05-05", null) })
{
    var queryHome = TestComponents.CreateHome(factory, new TestAuth("Q"));
    var initial = (DateTime)typeof(Home).GetField("expenseDate", flags)!.GetValue(queryHome)!;
    typeof(Home).GetProperty("DateParameter")!.SetValue(queryHome, raw);
    typeof(Home).GetProperty("UserDataProvisioner", flags)!.SetValue(queryHome, new UserDataProvisioner(factory));
    typeof(Home).GetProperty("RecurringExpenseService", flags)!.SetValue(queryHome, new RecurringExpenseService(factory));
    await (Task)typeof(Home).GetMethod("OnInitializedAsync", flags)!.Invoke(queryHome, null)!;
    var actual = (DateTime)typeof(Home).GetField("expenseDate", flags)!.GetValue(queryHome)!;
    Check(actual == (expected ?? initial), $"date query '{raw}' gave {actual:yyyy-MM-dd}");
}

// ── 실제 렌더링: 컴포넌트에 값이 제대로 전달되는지(문자열 리터럴로 잘못 전달하는 실수 방지) ──
await using (var db = new ExpensesDbContext(options))
{
    db.UserProfiles.Add(new UserProfile { OwnerId = "R", HasCompletedOnboarding = true });
    db.PaymentMethods.Add(new PaymentMethod { OwnerId = "R", Name = "렌더카드", Type = "신용카드" });
    db.Expenses.AddRange(
        new ExpenseRecord { OwnerId = "R", Date = today, Amount = 1_000, Category = "식비", Memo = "렌더메모" },
        new ExpenseRecord { OwnerId = "R", Date = today, Amount = 2_000, Category = "식비", Memo = "렌더메모" },
        new ExpenseRecord { OwnerId = "R", Date = today.AddDays(-1), Amount = 3_000, Category = "교통", Memo = "" });
    await db.SaveChangesAsync();
}
var services = new ServiceCollection().AddLogging()
    .AddSingleton<IDbContextFactory<ExpensesDbContext>>(factory)
    .AddScoped<AuthenticationStateProvider>(_ => new TestAuth("R"))
    .AddScoped<ExpenseService>().AddScoped<BudgetService>().AddScoped<CategoryService>().AddScoped<PaymentMethodService>()
    .AddScoped<ExpenseTemplateService>().AddScoped<RecurringExpenseService>().AddScoped<RecurringIncomeService>().AddScoped<UserDataProvisioner>()
    .AddScoped<NavigationManager, FakeNavigation>()
    .BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
async Task<string> Render<T>(Dictionary<string, object?>? parameters = null) where T : IComponent => await renderer.Dispatcher.InvokeAsync(async () =>
    System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? []))).ToHtmlString()));
var formHtml = await Render<MyExpenses.Components.Expenses.ExpenseForm>(new()
{
    ["Memo"] = "직접넘긴메모", ["Amount"] = 4_321L, ["Category"] = "식비", ["Categories"] = new[] { "식비", "교통" }, ["Date"] = new DateTime(2026, 10, 5),
    ["Suggestions"] = new List<MemoSuggestion> { new("커피", "카페", null, 4_500, 3) }, ["AutofillNotice"] = "자동으로 채웠어요"
});
Check(formHtml.Contains("value=\"직접넘긴메모\"") && (formHtml.Contains("value=\"커피\"") && formHtml.Contains(">카페 · 4,500원</option>")) && formHtml.Contains("자동으로 채웠어요") && formHtml.Contains("value=\"4321\""), "ExpenseForm parameters not rendered: " + System.Text.RegularExpressions.Regex.Match(formHtml, "<datalist.*?</datalist>", System.Text.RegularExpressions.RegexOptions.Singleline).Value + " | " + System.Text.RegularExpressions.Regex.Match(formHtml, "id=\"expense-amount\"[^>]*>").Value);
var listHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new()
{
    ["Expenses"] = new List<ExpenseRecord>
    {
        new() { Id = 1, OwnerId = "R", Date = today, Amount = 1_000, Category = "식비", Memo = "가" },
        new() { Id = 2, OwnerId = "R", Date = today, Amount = 2_000, Category = "식비", Memo = "나" },
        new() { Id = 3, OwnerId = "R", Date = today.AddDays(-1), Amount = 3_000, Category = "교통", Memo = "" },
    },
    ["GroupByDate"] = true, ["EditCategories"] = new[] { "식비" }
});
Check(System.Text.RegularExpressions.Regex.Matches(listHtml, "class=\"day-header\"").Count == 2 && listHtml.Contains("2건 · 3,000원") && listHtml.Contains("1건 · 3,000원"), "Day headers wrong");
Check(!(await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord> { new() { Id = 1, OwnerId = "R", Date = today, Amount = 1, Category = "식비", Memo = "" } }, ["GroupByDate"] = false })).Contains("day-header"), "Day headers shown while grouping is off");

// 내역 나눠 보기: 화면에는 일부만 그리고, 합계·건수·날짜별 합계는 전체 기준
string[] sameDay = ["가", "나", "다"];
var pagedHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new()
{
    ["Expenses"] = new List<ExpenseRecord>
    {
        new() { Id = 1, OwnerId = "R", Date = today, Amount = 1_000, Category = "식비", Memo = sameDay[0] },
        new() { Id = 2, OwnerId = "R", Date = today, Amount = 2_000, Category = "식비", Memo = sameDay[1] },
        new() { Id = 3, OwnerId = "R", Date = today, Amount = 4_000, Category = "식비", Memo = sameDay[2] },
        new() { Id = 4, OwnerId = "R", Date = today.AddDays(-1), Amount = 100, Category = "교통", Memo = "라" },
        new() { Id = 5, OwnerId = "R", Date = today.AddDays(-2), Amount = 200, Category = "교통", Memo = "마" }
    },
    ["GroupByDate"] = true, ["Limit"] = 2, ["PageSize"] = 2, ["EditCategories"] = new[] { "식비" }
});
Check(System.Text.RegularExpressions.Regex.Matches(pagedHtml, "class=\"expense-item\"").Count == 2, "Only Limit items must be rendered");
Check(pagedHtml.Contains("5건 중 2건을 표시하고 있어요") && pagedHtml.Contains("2건 더 보기") && pagedHtml.Contains("모두 보기"), "More footer wrong");
Check(pagedHtml.Contains("3건 · 7,000원"), "Day total must include hidden items of the same day");
Check(System.Text.RegularExpressions.Regex.Matches(pagedHtml, "class=\"day-header\"").Count == 1, "Day headers must only be shown for visible days");
var lastPageHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new()
{
    ["Expenses"] = new List<ExpenseRecord> { new() { Id = 1, OwnerId = "R", Date = today, Amount = 1, Category = "식비", Memo = "a" }, new() { Id = 2, OwnerId = "R", Date = today, Amount = 1, Category = "식비", Memo = "b" }, new() { Id = 3, OwnerId = "R", Date = today, Amount = 1, Category = "식비", Memo = "c" } },
    ["Limit"] = 2, ["PageSize"] = 50, ["EditCategories"] = new[] { "식비" }
});
Check(lastPageHtml.Contains("1건 더 보기") && !lastPageHtml.Contains("모두 보기"), "Last page footer wrong");
Check(!(await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord> { new() { Id = 1, OwnerId = "R", Date = today, Amount = 1, Category = "식비", Memo = "a" } }, ["Limit"] = 50, ["EditCategories"] = new[] { "식비" } })).Contains("더 보기"), "Footer shown although everything is visible");

// 입력 칩: 카테고리가 12개 이하면 칩, 그보다 많으면 선택 목록. 메모 칩은 최대 6개.
{
    var few = new[] { "식비", "교통", "쇼핑" };
    var memos = Enumerable.Range(1, 9).Select(i => new MemoSuggestion($"메모{i}", "식비", null, 1000 * i, 1)).ToList();
    var chipHtml = await Render<MyExpenses.Components.Expenses.ExpenseForm>(new() { ["Categories"] = few, ["Category"] = "교통", ["Suggestions"] = memos, ["Memo"] = "메모2" });
    Check(!chipHtml.Contains("id=\"expense-category\"") && chipHtml.Contains("role=\"radiogroup\""), "Few categories must render as chips");
    Check(System.Text.RegularExpressions.Regex.Matches(chipHtml, "role=\"radio\"").Count == 3 && chipHtml.Contains("aria-checked=\"true\""), "One chip per category, selected one marked");
    Check(System.Text.RegularExpressions.Regex.Matches(chipHtml, "class=\"choice-chip small").Count == 6, "Memo chips must be capped at 6");
    Check(chipHtml.Contains(">메모2<") && !chipHtml.Contains(">메모7<"), "Memo chips must keep the suggestion order");
    var manyHtml = await Render<MyExpenses.Components.Expenses.ExpenseForm>(new() { ["Categories"] = Enumerable.Range(1, 13).Select(i => $"분류{i}").ToArray(), ["Category"] = "분류1" });
    Check(manyHtml.Contains("id=\"expense-category\"") && !manyHtml.Contains("role=\"radiogroup\""), "Many categories must fall back to the select list");
    var noMemoHtml = await Render<MyExpenses.Components.Expenses.ExpenseForm>(new() { ["Categories"] = few, ["Category"] = "식비" });
    Check(!noMemoHtml.Contains("memo-chips"), "No memo chips without suggestions");

    // 지출 목록: 줄 전체가 누를 수 있고, 수정·삭제는 ⋯ 메뉴에만 있어야 합니다.
    var rowHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord> { new() { Id = 1, OwnerId = "R", Date = today, Amount = 5000, Category = "식비", Memo = "a" } }, ["EditCategories"] = new[] { "식비" } });
    Check(rowHtml.Contains("class=\"menu-button\"") && !rowHtml.Contains("class=\"edit-button\"") && !rowHtml.Contains("class=\"delete-button\""), "Edit/delete must live in the closed ⋯ menu");
    Check(rowHtml.Contains("title=\"눌러서 수정\"") && !rowHtml.Contains("class=\"edit-form\""), "Row must be tappable and no edit form while idle");
    var editHtml = await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord> { new() { Id = 1, OwnerId = "R", Date = today, Amount = 5000, Category = "식비", Memo = "a" } }, ["EditingId"] = 1, ["EditAmount"] = 5000L, ["EditCategory"] = "식비", ["EditDate"] = today, ["EditCategories"] = new[] { "식비" } });
    Check(editHtml.Contains("class=\"sheet-backdrop\"") && editHtml.Contains("role=\"dialog\"") && editHtml.Contains("id=\"edit-amount\""), "Edit mode must render the sheet with a backdrop");
}

// 공용 안내 컴포넌트: 로딩·빈 화면은 화면 낭독기가 읽을 수 있고 다음 행동을 보여 줘야 합니다.
{
    var loading = await Render<MyExpenses.Components.Shared.LoadingState>(new() { ["Text"] = "통계를 불러오고 있습니다..." });
    Check(loading.Contains("role=\"status\"") && loading.Contains("통계를 불러오고 있습니다...") && loading.Contains("aria-hidden=\"true\""), "Loading state must announce its text and hide the bars from readers");
    Check((await Render<MyExpenses.Components.Shared.LoadingState>(new())).Contains("불러오는 중입니다..."), "Loading state needs a default text");
    var empty = await Render<MyExpenses.Components.Shared.EmptyState>(new() { ["Title"] = "아직 없어요", ["Description"] = "설명", ["ActionText"] = "추가하기", ["ActionHref"] = "/?add=1", ["QuickAdd"] = true });
    Check(empty.Contains("아직 없어요") && empty.Contains("href=\"/?add=1\"") && empty.Contains("data-quick-add") && empty.Contains("추가하기"), "Empty state must show title and an action");
    var plain = await Render<MyExpenses.Components.Shared.EmptyState>(new() { ["Title"] = "비었어요" });
    Check(!plain.Contains("<a ") && !plain.Contains("<p>"), "Empty state without action/description must not render them");
    var firstUse = await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord>(), ["EditCategories"] = new[] { "식비" } });
    Check(firstUse.Contains("첫 지출 기록하기") && firstUse.Contains("data-quick-add"), "Empty expense list must invite the first entry");
    var noMatch = await Render<MyExpenses.Components.Expenses.ExpenseList>(new() { ["Expenses"] = new List<ExpenseRecord>(), ["FilterActive"] = true, ["EditCategories"] = new[] { "식비" } });
    Check(noMatch.Contains("조건에 맞는 기록이 없어요") && !noMatch.Contains("첫 지출 기록하기"), "Filtered empty list must explain the filter, not ask for a first entry");
}

// 화면 설정(다크 모드·글자 크기)과 건너뛰기 링크: 스크립트·마크업·스타일이 서로 맞아야 합니다.
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "MyExpenses.csproj"))) root = root.Parent;
    string Read(string path) => File.ReadAllText(Path.Combine(root!.FullName, path));
    var app = Read("Components/App.razor");
    Check(app.Contains("theme.css") && app.Contains("theme.js"), "App must load theme.css and theme.js");
    Check(app.IndexOf("theme.js", StringComparison.Ordinal) < app.IndexOf("<HeadOutlet", StringComparison.Ordinal) || app.IndexOf("theme.js", StringComparison.Ordinal) < app.IndexOf("<body>", StringComparison.Ordinal), "theme.js must load in <head> so the theme is set before first paint");
    var themeJs = Read("wwwroot/theme.js");
    var themeCss = Read("wwwroot/theme.css");
    Check(themeJs.Contains("data-theme-choice") && themeJs.Contains("data-theme-toggle") && themeJs.Contains("data-font-choice") && themeJs.Contains("prefers-color-scheme"), "theme.js lost a feature");
    Check(themeCss.Contains(":root[data-theme=\"dark\"]") && themeCss.Contains("--c-ffffff"), "theme.css must define dark tokens");
    var layout = Read("Components/Layout/MainLayout.razor");
    Check(layout.Contains("data-theme-toggle") && layout.Contains("href=\"#main-content\"") && layout.Contains("id=\"main-content\""), "Layout needs the theme toggle and a working skip link");
    var account = Read("Components/Pages/Settings/Account.razor");
    foreach (var choice in new[] { "data-theme-choice=\"system\"", "data-theme-choice=\"light\"", "data-theme-choice=\"dark\"", "data-font-choice=\"normal\"", "data-font-choice=\"large\"" })
        Check(account.Contains(choice), "Account display settings lost " + choice);
    Check(Read("wwwroot/app.css").Contains("prefers-reduced-motion") && Read("wwwroot/app.css").Contains("focus-visible"), "Global focus ring and reduced-motion rules are required");
}

// 하단 탭바 "지출 추가": 링크·스크립트·입력 칸·쿼리 매개변수가 서로 어긋나지 않아야 합니다.
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "MyExpenses.csproj"))) root = root.Parent;
    string Read(string path) => File.ReadAllText(Path.Combine(root!.FullName, path));
    var addQuery = typeof(Home).GetProperty("AddParameter")?.GetCustomAttributes(typeof(SupplyParameterFromQueryAttribute), false).Cast<SupplyParameterFromQueryAttribute>().SingleOrDefault();
    Check(addQuery?.Name == "add", "Home must read ?add= to focus the amount field");
    var bottomNav = Read("Components/Layout/BottomNav.razor");
    Check(bottomNav.Contains("href=\"/?add=1\"") && bottomNav.Contains("data-quick-add"), "Bottom nav add tab must link to /?add=1 and carry data-quick-add");
    foreach (var tab in new[] { "href=\"\"", "href=\"calendar\"", "href=\"statistics\"", ".navbar-toggler" })
        Check(bottomNav.Contains(tab), "Bottom nav lost a tab: " + tab);
    Check(Read("Components/Layout/MainLayout.razor").Contains("<BottomNav />"), "MainLayout must render the bottom nav");
    Check(Read("Components/App.razor").Contains("quick-add.js"), "App must load quick-add.js");
    var quickAdd = Read("wwwroot/quick-add.js");
    Check(quickAdd.Contains("[data-quick-add]") && quickAdd.Contains("expense-amount"), "quick-add.js must target the add tab and the amount field");
    Check(Read("Components/Expenses/ExpenseForm.razor").Contains("id=\"expense-amount\""), "Amount input id changed; quick-add.js depends on it");
    var homeMarkup = Read("Components/Pages/Expenses/Home.razor");
    Check(homeMarkup.Contains("id=\"budget-section\"") && homeMarkup.Contains("href=\"#budget-section\""), "Budget card must point at the collapsed budget section");
}

// 화면 조립 가드: 문자열 매개변수에 변수 이름을 @ 없이 넘기면 변수가 아니라 그 글자가 전달됩니다(예: Memo="memo").
var componentTypes = typeof(Home).Assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t)).ToDictionary(t => t.Name);
var markupFiles = new[] { "Components/Pages/Expenses/Home.razor" };
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "MyExpenses.csproj"))) repo = repo.Parent;
foreach (var file in markupFiles)
{
    var markup = File.ReadAllText(Path.Combine(repo!.FullName, file));
    foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(markup, @"<([A-Z]\w+)\s([^>]*)>"))
    {
        if (!componentTypes.TryGetValue(tag.Groups[1].Value, out var type)) continue;
        foreach (System.Text.RegularExpressions.Match attribute in System.Text.RegularExpressions.Regex.Matches(tag.Groups[2].Value, "(?<![-\\w@])([A-Z]\\w*)=\"([^\"]*)\""))
        {
            var property = type.GetProperty(attribute.Groups[1].Value);
            if (property is null || property.PropertyType != typeof(string)) continue;
            var value = attribute.Groups[2].Value;
            Check(value.StartsWith('@') || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z][A-Za-z0-9]*$"),
                $"{file}: {type.Name}.{property.Name}=\"{value}\" is passed as literal text; use @{value}");
        }
    }
}

// ── 내역 나눠 보기: 지출 화면 논리 ──
await using (var pagingDb = new ExpensesDbContext(options))
{
    pagingDb.UserProfiles.Add(new UserProfile { OwnerId = "PG", HasCompletedOnboarding = true });
    for (var i = 0; i < 120; i++)
        pagingDb.Expenses.Add(new ExpenseRecord { OwnerId = "PG", Date = today.AddDays(-(i / 3)), Amount = 1_000 + i, Category = "식비", Memo = $"기록{i:000}" });
    await pagingDb.SaveChangesAsync();
}
var pagingAuth = new TestAuth("PG");
var paging = TestComponents.CreateHome(factory, pagingAuth);
void PSet(string name, object? value) => typeof(Home).GetField(name, flags)!.SetValue(paging, value);
T PGet<T>(string name) => (T)typeof(Home).GetField(name, flags)!.GetValue(paging)!;
Task PCall(string method, params object[] args) => (Task)typeof(Home).GetMethod(method, flags)!.Invoke(paging, args)!;
object? PCallSync(string method, params object[] args) => typeof(Home).GetMethod(method, flags)!.Invoke(paging, args);
await PCall("LoadCategoriesAsync"); await PCall("LoadExpensesAsync");
var allTotal = PGet<List<ExpenseRecord>>("expenses").Sum(e => e.Amount);
Check(PGet<List<ExpenseRecord>>("expenses").Count == 120 && PGet<int>("visibleCount") == 50, "Initial page size wrong");
PCallSync("ShowMore"); Check(PGet<int>("visibleCount") == 100, "Show more wrong");
PCallSync("ShowMore"); Check(PGet<int>("visibleCount") == 120, "Show more must stop at the total");
PCallSync("ShowMore"); Check(PGet<int>("visibleCount") == 120, "Show more past the end");
Check(PGet<List<ExpenseRecord>>("expenses").Sum(e => e.Amount) == allTotal && (decimal)typeof(Home).GetProperty("FilteredTotal", flags)!.GetValue(paging)! == allTotal, "Totals must not depend on how many rows are shown");
await PCall("ApplyFiltersAsync");
Check(PGet<int>("visibleCount") == 50, "A new search must go back to the first page");
PCallSync("ShowAll"); Check(PGet<int>("visibleCount") == 120, "Show all wrong");
await PCall("ShiftMonthAsync", -1);
Check(PGet<int>("visibleCount") == 50, "Month paging must go back to the first page");
await PCall("ResetFiltersAsync");
// 적은 건수에서는 더 보기가 PageSize 아래로 내려가지 않음
PSet("searchInput", new ExpenseSearchInput { Search = "기록00" });
await PCall("ApplyFiltersAsync");
Check(PGet<List<ExpenseRecord>>("expenses").Count == 10 && PGet<int>("visibleCount") == 50, "Small result must keep the page size");
PCallSync("ShowMore"); Check(PGet<int>("visibleCount") == 50, "Show more on a small result");
await PCall("ResetFiltersAsync");
// 새 기록이 보이지 않는 위치에 들어가면 그 기록까지 넓혀서 보여 줌
PSet("expenseDate", today.AddDays(-200)); PSet("amount", 777L); PSet("category", "식비"); PSet("memo", "아주 오래된 기록");
await PCall("AddExpenseAsync");
var added = PGet<List<ExpenseRecord>>("expenses").FindIndex(e => e.Memo == "아주 오래된 기록");
Check(added == 120 && PGet<int>("visibleCount") >= 121, $"A new record at index {added} must be visible (visible={PGet<int>("visibleCount")})");
await PCall("ResetFiltersAsync");
// 수정으로 맨 뒤로 밀린 기록도 보이게 유지
var firstRecord = PGet<List<ExpenseRecord>>("expenses")[0];
PCallSync("StartEdit", firstRecord);
PSet("editDate", today.AddDays(-300));
await PCall("SaveEditAsync");
var moved = PGet<List<ExpenseRecord>>("expenses").FindIndex(e => e.Id == firstRecord.Id);
Check(moved >= 100 && PGet<int>("visibleCount") >= moved + 1, "An edited record moved down the list must stay visible");
await PCall("ResetFiltersAsync");
// 보이는 범위의 경계: 마지막으로 보이는 칸(49)은 그대로, 첫 번째 숨은 칸(50)은 정확히 하나만 넓힘
PSet("visibleCount", 50);
PCallSync("ShowThrough", PGet<List<ExpenseRecord>>("expenses")[49].Id);
Check(PGet<int>("visibleCount") == 50, "A visible record must not widen the list");
PCallSync("ShowThrough", PGet<List<ExpenseRecord>>("expenses")[50].Id);
Check(PGet<int>("visibleCount") == 51, "The first hidden record must widen the list by exactly one");
PCallSync("ShowThrough", -12345);
Check(PGet<int>("visibleCount") == 51, "An unknown id must not change the list");
// 지운 기록을 되돌릴 때: 오래된순에서는 같은 날짜의 맨 뒤로 가므로 쪽 경계를 넘을 수 있음
PSet("searchInput", new ExpenseSearchInput { Sort = "Oldest" });
await PCall("ApplyFiltersAsync");
var oldestFirst = PGet<List<ExpenseRecord>>("expenses");
// 같은 날짜의 기록이 쪽 경계에 걸치도록 보이는 건수를 맞춥니다(k-1은 보이고 k는 숨김, 둘은 같은 날짜).
var k = Enumerable.Range(30, 80).First(index => oldestFirst[index - 1].Date == oldestFirst[index].Date);
PSet("visibleCount", k);
var boundary = oldestFirst[oldestFirst.FindIndex(e => e.Date == oldestFirst[k].Date)];
Check(oldestFirst.IndexOf(boundary) < k, "Test setup: the record must be visible before it is deleted");
await PCall("DeleteExpenseAsync", boundary.Id);
await PCall("UndoDeleteAsync");
var restoredIndex = PGet<List<ExpenseRecord>>("expenses").FindIndex(e => e.Memo == boundary.Memo);
Check(restoredIndex >= k - 1 && PGet<int>("visibleCount") >= restoredIndex + 1, $"A restored record that crosses the page boundary must stay visible (index {restoredIndex}, visible {PGet<int>("visibleCount")}, k {k})");
await PCall("ResetFiltersAsync");

// ── 달력 화면 논리 ──
var calAuth = new TestAuth("H");
var page = new Calendar();
void Inject(string name, object value) => typeof(Calendar).GetProperty(name, flags)!.SetValue(page, value);
Inject("AuthenticationStateProvider", calAuth);
Inject("CalendarService", calendar);
Inject("RecurringExpenseService", new RecurringExpenseService(factory));
Inject("RecurringIncomeService", new RecurringIncomeService(factory));
Inject("Logger", NullLogger<Calendar>.Instance);
T CalGet<T>(string name) => (T)typeof(Calendar).GetField(name, flags)!.GetValue(page)!;
Task CalCall(string method, params object[] args) => (Task)typeof(Calendar).GetMethod(method, flags)!.Invoke(page, args)!;
await CalCall("OnInitializedAsync");
var thisMonth = new DateOnly(today.Year, today.Month, 1);
Check(CalGet<DateOnly>("month") == thisMonth && CalGet<DateOnly?>("selected") == DateOnly.FromDateTime(today) && CalGet<CalendarData?>("data") is not null, "Calendar initial state wrong");
await CalCall("ShiftAsync", -1);
Check(CalGet<DateOnly>("month") == thisMonth.AddMonths(-1) && CalGet<DateOnly?>("selected") is null, "Calendar shift must clear selection");
await CalCall("ShiftAsync", 1);
Check(CalGet<DateOnly?>("selected") == DateOnly.FromDateTime(today), "Returning to this month must select today");
typeof(Calendar).GetMethod("Select", flags)!.Invoke(page, [thisMonth]);
Check(CalGet<DateOnly?>("selected") == thisMonth, "Select failed");
typeof(Calendar).GetMethod("Select", flags)!.Invoke(page, [thisMonth]);
Check(CalGet<DateOnly?>("selected") is null, "Selecting again must deselect");
await CalCall("ShiftAsync", -2);
await CalCall("GoTodayAsync");
Check(CalGet<DateOnly>("month") == thisMonth, "Go today wrong");
typeof(Calendar).GetField("month", flags)!.SetValue(page, CalendarMonth.MinimumMonth);
await CalCall("ShiftAsync", -1);
Check(CalGet<DateOnly>("month") == CalendarMonth.MinimumMonth, "Shift below minimum must be ignored");
calAuth.OwnerId = "OTHER";
await CalCall("ReloadAsync");
Check(CalGet<CalendarData?>("data") is null && CalGet<string?>("errorMessage") is { Length: > 0 }, "Calendar must block an account change");

Console.WriteLine("PASS: memo suggestions, quick entry, month navigation, undo delete, history paging, calendar calculation, service and page logic");

sealed class FakeNavigation : NavigationManager
{
    public FakeNavigation() => Initialize("http://localhost/", "http://localhost/");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}

sealed class StaticHome : Home { }
sealed class StaticCalendar : MyExpenses.Components.Pages.Calendar { }
