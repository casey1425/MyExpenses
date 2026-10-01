using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
await ExpensesSchema.EnsureCreatedAsync(db);
var factory = new TestFactory(options);
var expenses = new ExpenseService(factory);
var budgets = new BudgetService(factory);
var month = new DateTime(2026, 10, 1);
var first = await expenses.AddAsync("A", new(month.AddDays(2), 4500, "카페", " 커피 "));
var foreign = await expenses.AddAsync("B", new(month, 10000, "식비", "other"));
Check(first.Memo == "커피", "Normalization changed");
Check((await expenses.ListAsync("A", new())).Count == 1, "Query ownership failed");
Check(await expenses.UpdateAsync("A", foreign.Id, new(month, 1, "카페", "")) is null, "Foreign update accepted");
Check(!await expenses.DeleteAsync("A", foreign.Id), "Foreign deletion accepted");
Check((await expenses.UpdateAsync("A", first.Id, new(month, 5000, "카페", "edit")))?.Amount == 5000, "Update failed");
await Reject(() => expenses.AddAsync("", new(month, 1, "카페", "")));
await Reject(() => expenses.AddAsync("A", new(month, 0, "카페", "")));
await Reject(() => expenses.AddAsync("A", new(month, 1, "invalid", "")));
await Reject(() => expenses.AddAsync("A", new(month, 1, "카페", new string('a', 101))));
await budgets.SaveAsync("A", month.AddDays(3), 100000);
await budgets.SaveAsync("A", month, 20000, "카페");
await budgets.SaveAsync("B", month, 900000);
var snapshot = await budgets.LoadAsync("A", month);
Check(snapshot.Amount == 100000 && snapshot.Spent == 5000 && snapshot.CategoryAmounts["카페"] == 20000 && snapshot.CategorySpent["카페"] == 5000, "Budget snapshot failed");
await budgets.SaveAsync("A", month, 30000, "카페");
Check((await budgets.LoadAsync("A", month)).CategoryAmounts["카페"] == 30000, "Category budget update failed");
await budgets.DeleteAsync("A", month, "카페");
Check((await budgets.LoadAsync("A", month)).Amount == 100000 && (await budgets.LoadAsync("A", month)).CategoryAmounts.Count == 0, "Independent budget deletion failed");
Check((await budgets.LoadAsync("B", month)).Amount == 900000, "Budget ownership failed");
await Reject(() => budgets.SaveAsync("A", month, -1));
await Reject(() => budgets.SaveAsync("A", month, 1, "invalid"));
await Reject(() => budgets.LoadAsync("", month));

var auth = new TestAuth();
var home = TestComponents.CreateHome(factory, auth);
var flags = BindingFlags.NonPublic | BindingFlags.Instance;
typeof(Home).GetField("budgetMonth", flags)!.SetValue(home, month);
typeof(Home).GetField("budgetInput", flags)!.SetValue(home, 200000L);
await (Task)typeof(Home).GetMethod("SaveBudgetAsync", flags)!.Invoke(home, null)!;
Check((await budgets.LoadAsync("A", month)).Amount == 200000, "Budget UI callback failed");
auth.OwnerId = "B";
typeof(Home).GetField("budgetInput", flags)!.SetValue(home, 300000L);
await (Task)typeof(Home).GetMethod("SaveBudgetAsync", flags)!.Invoke(home, null)!;
Check((await budgets.LoadAsync("A", month)).Amount == 200000, "Changed identity allowed budget mutation");
await new ExpenseTemplateService(factory).SaveAsync("A", null, new() { Name = "coffee", Amount = "5000", Category = "카페" });
Check(await expenses.DeleteAllAsync("A") == 1, "Delete all failed");
Check((await expenses.ListAsync("B", new())).Count == 1, "Delete all leaked owners");
Check((await budgets.LoadAsync("A", month)).Amount == 200000 && await db.ExpenseTemplates.CountAsync() == 1, "Delete all damaged budgets or templates");
await budgets.DeleteAsync("A", month);
Check((await budgets.LoadAsync("A", month)).Amount is null, "Monthly budget deletion failed");
Check(ExpenseCategories.All.SequenceEqual(ExpenseTemplateInput.Categories) && ExpenseCategories.All.All(ExpenseCategories.IsSupported), "Shared categories inconsistent");
Console.WriteLine("PASS: expense and budget services, query/write isolation, validation, independent budget deletion, UI callback and identity change protection");
