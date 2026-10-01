using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using var db = new ExpensesDbContext(options);
await db.Database.EnsureCreatedAsync();
// Simulate upgrading a pre-category DB. Existing records must remain intact.
db.Expenses.Add(new() { OwnerId = "A", Category = "식비", Amount = 1000, Date = DateTime.Today });
await db.SaveChangesAsync();
await db.Database.ExecuteSqlRawAsync("DROP TABLE UserCategories");
await ExpensesSchema.EnsureCreatedAsync(db);
await ExpensesSchema.EnsureCreatedAsync(db);
var factory = new TestFactory(options);
var categories = new CategoryService(factory);
var expenses = new ExpenseService(factory);
var budgets = new BudgetService(factory);
var templates = new ExpenseTemplateService(factory);
var recurring = new RecurringExpenseService(factory);
var csv = new ExpenseCsvImportService(factory);
Check((await categories.ListAsync("A")).Select(c => c.Name).SequenceEqual(ExpenseCategories.All), "Legacy default initialization failed");
Check((await categories.ListAsync("B")).Count == 6, "New user defaults failed");
await categories.SaveAsync("A", null, " 구독 ");
var custom = (await categories.ListAsync("A")).Single(c => c.Name == "구독");
Check(!(await categories.ListAsync("B")).Any(c => c.Name == "구독"), "Categories leaked owners");
await Reject(() => categories.SaveAsync("A", null, "구독"));
await Reject(() => categories.SaveAsync("A", null, " "));
await Reject(() => categories.SaveAsync("A", null, new string('a', 31)));
await Reject(() => categories.SaveAsync("A", null, "a\nb"));
await Reject(() => categories.SaveAsync("B", custom.Id, "stolen"));
await Reject(() => categories.ArchiveAsync("B", custom.Id, true));
await Reject(() => categories.MoveAsync("B", custom.Id, -1));
await Reject(() => expenses.AddAsync("B", new(DateTime.Today, 100, "구독", "")));
await Reject(() => templates.SaveAsync("B", null, new() { Name = "bad", Amount = "100", Category = "구독" }));
await Reject(() => recurring.CreateAsync("B", 1, 100, "구독", ""));
await Reject(() => budgets.SaveAsync("B", DateTime.Today, 100, "구독"));
var expense = await expenses.AddAsync("A", new(DateTime.Today, 10000, "구독", "monthly"));
await budgets.SaveAsync("A", DateTime.Today, 20000, "구독");
await templates.SaveAsync("A", null, new() { Name = "subscription", Amount = "10000", Category = "구독" });
await recurring.CreateAsync("A", 1, 1000, "구독", "rule");
await categories.SaveAsync("A", custom.Id, "정기 구독");
Check((await expenses.ListAsync("A", new(Category: "정기 구독"))).Single().Id == expense.Id, "Rename expenses or filter failed");
Check((await budgets.LoadAsync("A", DateTime.Today)).CategoryAmounts["정기 구독"] == 20000, "Rename budget failed");
Check((await templates.ListAsync("A")).Single().Category == "정기 구독", "Rename template failed");
Check((await recurring.ListAsync("A")).Single().Category == "정기 구독", "Rename recurring failed");
Check(!(await categories.ListAsync("A")).Any(c => c.Name == "구독"), "Old name re-seeded");
var original = (await categories.ListAsync("A")).Single(c => c.Name == "식비");
await Reject(() => categories.SaveAsync("A", custom.Id, original.Name));
Check((await expenses.ListAsync("A", new(Category: "정기 구독"))).Count == 1, "Rejected rename modified data");
await categories.SaveAsync("A", original.Id, "외식");
Check(!(await new CategoryService(factory).ListAsync("A")).Any(c => c.Name == "식비"), "Renamed default re-created");
Check((await categories.ListAsync("B")).Any(c => c.Name == "식비"), "Default rename leaked owner");
Check((await expenses.ListAsync("A", new(Category: "외식"))).Single().Amount == 1000, "Legacy expense rename failed");
var today = DateOnly.FromDateTime(DateTime.Today);
var report = await new ExpenseTrendsService(factory).LoadAsync("A", ExpenseTrends.MonthStart(today), today);
Check(report.Categories.Any(c => c.Category == "정기 구독" && c.Comparison.CurrentAmount == 10000), "Custom category trends failed");
await categories.MoveAsync("A", custom.Id, -1);
Check((await categories.ListAsync("A")).FindIndex(c => c.Id == custom.Id) == 5, "Ordering failed");
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<AuthenticationStateProvider>(new TestAuth("A"));
services.AddSingleton(categories);
services.AddSingleton(new CategoryRenderHarness(new TestAuth("A"), categories));
await using (var provider = services.BuildServiceProvider())
await using (var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>()))
{
    var html = await renderer.Dispatcher.InvokeAsync(async () =>
        System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<StaticCategories>(ParameterView.Empty)).ToHtmlString()));
    Check(html.Contains("정기 구독") && html.Contains("외식") && html.Contains("이름 변경") && html.Contains("보관"), "Category management page render failed");
    Check(!html.Contains(">식비<"), "Foreign/default category rendered");
}
await categories.ArchiveAsync("A", custom.Id, true);
await Reject(() => expenses.AddAsync("A", new(DateTime.Today, 1, "정기 구독", "")));
await Reject(() => templates.SaveAsync("A", null, new() { Name = "bad", Amount = "1", Category = "정기 구독" }));
await Reject(() => recurring.CreateAsync("A", 1, 1, "정기 구독", ""));
Check((await expenses.UpdateAsync("A", expense.Id, new(DateTime.Today, 10000, "정기 구독", "edited"))) is not null, "Archived existing expense edit failed");
Check((await expenses.ListAsync("A", new(Category: "정기 구독"))).Count == 1, "Archived history hidden");
Check(await recurring.GenerateDueAsync("A", DateTime.Today) == 1, "Archived existing recurring stopped");
Check(await recurring.GenerateDueAsync("A", DateTime.Today) == 0, "Duplicate recurring generated");
var rows = new[] { new ExpenseCsvRow(2, DateTime.Today, 50, "정기 구독", "import") };
await Reject(() => csv.ImportAsync("A", rows, false));
await categories.ArchiveAsync("A", custom.Id, false);
Check(await csv.ImportAsync("A", rows, false) == 1, "Custom category CSV import failed");
await Reject(() => csv.ImportAsync("B", rows, false));
var bytes = ExpenseCsvExporter.Create(await expenses.ListAsync("A", new(Category: "정기 구독")));
using var reader = new StreamReader(new MemoryStream(bytes));
var parsed = ExpenseCsvImporter.Parse(reader, (await categories.ListAsync("A")).Select(c => c.Name).ToList());
Check(parsed.Issues.Count == 0 && parsed.Rows.Count == 3, "Custom CSV round trip failed");
await categories.SaveAsync("A", null, "=formula");
var formula = await expenses.AddAsync("A", new(DateTime.Today, 1, "=formula", ""));
using var formulaReader = new StreamReader(new MemoryStream(ExpenseCsvExporter.Create([formula])));
Check(ExpenseCsvImporter.Parse(formulaReader, ["=formula"]).Rows.Single().Category == "=formula", "CSV category formula protection round trip failed");
foreach (var c in (await categories.ListAsync("A")).Where(c => c.Id != custom.Id))
    await categories.ArchiveAsync("A", c.Id, true);
await Reject(() => categories.ArchiveAsync("A", custom.Id, true));
await new UserDataDeletionService(factory).DeleteAsync("A");
Check(!await db.UserCategories.AnyAsync(c => c.OwnerId == "A"), "Account deletion left categories");
Check(await db.UserCategories.CountAsync(c => c.OwnerId == "B") == 6, "Account deletion affected another user");
Console.WriteLine("PASS: category migration, defaults, CRUD validation, ownership, atomic rename, ordering, archive/restore, historical edits, recurring, budgets, trends, CSV and account deletion");

// InteractiveServer 페이지를 직접 정적 렌더러에 넣을 수 없어
// 화면 초기화와 마크업을 테스트 호스트에 위임합니다. 운영 컴포넌트는 변경하지 않습니다.
public sealed class StaticCategories : ComponentBase
{
    [Inject] public CategoryRenderHarness Harness { get; set; } = null!;
    protected override Task OnInitializedAsync() => Harness.InitializeAsync();
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => Harness.Render(builder);
}

public sealed class CategoryRenderHarness : Categories
{
    public CategoryRenderHarness(TestAuth authentication, CategoryService service)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        typeof(Categories).GetProperty("AuthenticationStateProvider", flags)!.SetValue(this, authentication);
        typeof(Categories).GetProperty("CategoryService", flags)!.SetValue(this, service);
        typeof(Categories).GetProperty("Logger", flags)!.SetValue(this, Microsoft.Extensions.Logging.Abstractions.NullLogger<Categories>.Instance);
    }
    public Task InitializeAsync() => base.OnInitializedAsync();
    public void Render(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => base.BuildRenderTree(builder);
}
