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

Console.WriteLine("PASS: memo suggestions, quick entry, month navigation, undo delete, calendar calculation, service and page logic");

sealed class FakeNavigation : NavigationManager
{
    public FakeNavigation() => Initialize("http://localhost/", "http://localhost/");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}

sealed class StaticHome : Home { }
sealed class StaticCalendar : MyExpenses.Components.Pages.Calendar { }
