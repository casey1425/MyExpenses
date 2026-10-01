using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;
using MyExpenses.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Security.Claims;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;
await using var db = new ExpensesDbContext(options);
await db.Database.EnsureCreatedAsync();
db.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new(2026, 10, 1), Amount = 1000, Category = "교통", Memo = "existing" });
db.MonthlyBudgets.Add(new MonthlyBudget { OwnerId = "A", Month = new(2026, 10, 1), Amount = 100000 });
await db.SaveChangesAsync();
// 기존 버전처럼 템플릿 테이블이 없는 DB를 재현합니다.
await db.Database.ExecuteSqlRawAsync("DROP TABLE ExpenseTemplates");
await BudgetSchema.EnsureCreatedAsync(db);
await BudgetSchema.EnsureCreatedAsync(db);
Check(await db.Expenses.CountAsync() == 1 && await db.MonthlyBudgets.CountAsync() == 1, "Upgrade changed existing data");
var factory = new TestFactory(options);
var service = new ExpenseTemplateService(factory);
var input = new ExpenseTemplateInput { Name = " 아메리카노 ", Amount = "4500", Category = "카페", Memo = " 포장 " };
Check(await service.SaveAsync("A", null, input), "Create failed");
Check(await service.SaveAsync("B", null, input), "Owner B create failed");
var template = (await service.ListAsync("A")).Single();
Check(template.Name == "아메리카노" && template.Memo == "포장", "Trimming failed");
Check(await service.FindAsync("B", template.Id) is null, "Owner isolation read failed");
Check(!await service.SaveAsync("B", template.Id, input), "Owner isolation update failed");
Check(!await service.DeleteAsync("B", template.Id), "Owner isolation delete failed");
input.Amount = "5500";
Check(await service.SaveAsync("A", template.Id, input), "Update failed");
Check((await service.FindAsync("A", template.Id))?.Amount == 5500, "Updated values missing");
var home = new Home();
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
typeof(Home).GetProperty("TemplateService", flags)!.SetValue(home, service);
typeof(Home).GetProperty("AuthenticationStateProvider", flags)!.SetValue(home, new TestAuth());
typeof(Home).GetProperty("Logger", flags)!.SetValue(home, NullLogger<Home>.Instance);
typeof(Home).GetField("ownerId", flags)!.SetValue(home, "A");
var selectedDate = new DateTime(2026, 9, 15);
typeof(Home).GetField("expenseDate", flags)!.SetValue(home, selectedDate);
async Task ApplyTemplate(int id) => await (Task)typeof(Home).GetMethod("ApplyTemplateAsync", flags)!.Invoke(home, new object[] { new ChangeEventArgs { Value = id.ToString() } })!;
await ApplyTemplate(template.Id);
Check((long)typeof(Home).GetField("amount", flags)!.GetValue(home)! == 5500, "Template amount not applied");
Check((string)typeof(Home).GetField("category", flags)!.GetValue(home)! == "카페", "Template category not applied");
Check((string)typeof(Home).GetField("memo", flags)!.GetValue(home)! == "포장", "Template memo not applied");
Check((DateTime)typeof(Home).GetField("expenseDate", flags)!.GetValue(home)! == selectedDate, "Template changed date");
await ApplyTemplate((await service.ListAsync("B")).Single().Id);
Check((long)typeof(Home).GetField("amount", flags)!.GetValue(home)! == 5500, "Foreign template changed form");
Check(await db.Expenses.CountAsync() == 1, "Template editing created an expense");
Check(await service.DeleteAsync("A", template.Id), "Delete failed");
Check(await service.FindAsync("A", template.Id) is null, "Deleted template still available");
await ApplyTemplate(template.Id);
Check(typeof(Home).GetField("templateError", flags)!.GetValue(home) is string, "Stale template missing error");
Check(await db.Expenses.CountAsync() == 1, "Template deletion changed recorded expenses");
Check(await service.SaveAsync("A", null, input), "Recreate failed");
await new UserDataDeletionService(factory).DeleteAsync("A");
Check((await service.ListAsync("A")).Count == 0 && await db.Expenses.CountAsync() == 0, "Account deletion failed");
Check((await service.ListAsync("B")).Count == 1, "Account deletion leaked across owners");
var invalid = new[]
{
    new ExpenseTemplateInput { Name = " " },
    new ExpenseTemplateInput { Name = new string('a', 51), Amount = "1" },
    new ExpenseTemplateInput { Name = "n", Amount = "0" },
    new ExpenseTemplateInput { Name = "n", Amount = "-1" },
    new ExpenseTemplateInput { Name = "n", Amount = "1.5" },
    new ExpenseTemplateInput { Name = "n", Amount = "9223372036854775808" },
    new ExpenseTemplateInput { Name = "n", Amount = "1", Category = "invalid" },
    new ExpenseTemplateInput { Name = "n", Amount = "1", Memo = new string('a', 101) }
};
foreach (var value in invalid)
{
    try { await service.SaveAsync("B", null, value); throw new Exception("Invalid input accepted"); }
    catch (ArgumentException) { }
}
try { await service.ListAsync(""); throw new Exception("Empty owner accepted"); }
catch (ArgumentException) { }
Check((await service.ListAsync("B")).Count == 1, "Invalid input persisted");
Check(new ExpenseTemplateInput { Name = "n", Amount = long.MaxValue.ToString() }.Validate().Amount == long.MaxValue, "Amount boundary failed");
Console.WriteLine("PASS: schema upgrade, CRUD persistence, owner isolation, stale ID, input validation, account deletion and expense preservation");

sealed class TestFactory(DbContextOptions<ExpensesDbContext> options) : IDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext() => new(options);
    public Task<ExpensesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}

sealed class TestAuth : AuthenticationStateProvider
{
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "A") }, "test"))));
}
