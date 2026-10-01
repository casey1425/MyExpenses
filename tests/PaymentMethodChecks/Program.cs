using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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
    try { await action(); throw new Exception("Invalid operation accepted"); }
    catch (ArgumentException) { }
}

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
            CREATE TABLE ExpenseTemplates (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId TEXT NOT NULL, Name TEXT NOT NULL, Amount INTEGER NOT NULL, Category TEXT NOT NULL, Memo TEXT NOT NULL);
            INSERT INTO Expenses (OwnerId,Date,Amount,Category,Memo) VALUES ('A','2026-10-01 00:00:00',1000,'교통','existing');
            """);
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
        db.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new(2026, 10, 1), Amount = 1000, Category = "교통", Memo = "existing" });
        await db.SaveChangesAsync();
    }
    await ExpensesSchema.EnsureCreatedAsync(db);
    await ExpensesSchema.EnsureCreatedAsync(db);
    Check((await db.Expenses.AsNoTracking().SingleAsync()).PaymentMethodId is null, "Legacy record changed");
    var factory = new TestFactory(options);
    var service = new PaymentMethodService(factory);
    await service.SaveAsync("A", null, " 국민 체크카드 ", "체크카드");
    await service.SaveAsync("B", null, "국민 체크카드", "체크카드");
    await service.SaveAsync("A", null, "현금", "현금");
    var card = (await service.ListAsync("A")).Single(m => m.Type == "체크카드");
    var cash = (await service.ListAsync("A")).Single(m => m.Type == "현금");
    var foreign = (await service.ListAsync("B")).Single();
    Check(card.Name == "국민 체크카드", "Name trim failed");
    Check(!await service.SaveAsync("B", card.Id, "changed", "현금"), "Foreign update accepted");
    Check(!await service.DeleteAsync("B", card.Id), "Foreign delete accepted");
    await Reject(() => service.SaveAsync("A", null, "국민 체크카드", "신용카드"));
    await Reject(() => service.SaveAsync("A", null, " ", "현금"));
    await Reject(() => service.SaveAsync("A", null, new string('a', 51), "현금"));
    await Reject(() => service.SaveAsync("A", null, "test", "invalid"));
    await Reject(() => service.ListAsync(""));

    var home = TestComponents.CreateHome(factory);
    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    void SetField(string name, object? value) => typeof(Home).GetField(name, flags)!.SetValue(home, value);
    object? Field(string name) => typeof(Home).GetField(name, flags)!.GetValue(home);
    async Task Invoke(string method, params object[] args) => await (Task)typeof(Home).GetMethod(method, flags)!.Invoke(home, args)!;
    var templates = new ExpenseTemplateService(factory);
    SetField("ownerId", "A"); SetField("expenseDate", new DateTime(2026, 10, 3));
    SetField("amount", 4500L); SetField("category", "카페"); SetField("memo", "커피"); SetField("paymentMethodId", card.Id);
    await Invoke("AddExpenseAsync");
    Check(await db.Expenses.CountAsync() == 2, "Expense create failed");
    var expense = await db.Expenses.AsNoTracking().SingleAsync(e => e.PaymentMethodId == card.Id);
    SetField("amount", 100L); SetField("paymentMethodId", foreign.Id);
    await Invoke("AddExpenseAsync");
    Check(await db.Expenses.CountAsync() == 2 && Field("errorMessage") is string, "Foreign payment accepted for expense");
    SetField("editingId", expense.Id); SetField("editDate", expense.Date); SetField("editAmount", 4800L);
    SetField("editCategory", "카페"); SetField("editMemo", "커피"); SetField("editPaymentMethodId", cash.Id);
    await Invoke("SaveEditAsync");
    expense = await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expense.Id);
    Check(expense.PaymentMethodId == cash.Id && expense.Amount == 4800, "Expense edit failed");
    var input = new ExpenseTemplateInput { Name = "커피", Amount = "4800", Category = "카페", Memo = "커피", PaymentMethodId = cash.Id };
    await templates.SaveAsync("A", null, input);
    await Reject(() => templates.SaveAsync("B", null, input));
    var template = (await templates.ListAsync("A")).Single();
    await Invoke("ApplyTemplateAsync", new ChangeEventArgs { Value = template.Id.ToString() });
    Check((int?)Field("paymentMethodId") == cash.Id && await db.Expenses.CountAsync() == 2, "Template payment application failed");

    var filter = new ExpenseFilter(Month: new(2026, 10, 1), PaymentMethodId: cash.Id);
    var owned = db.Expenses.AsNoTracking().Where(e => e.OwnerId == "A");
    Check(await filter.ApplyTo(owned).CountAsync() == 1 && filter.Matches(expense), "Payment filter failed");
    Check(await new ExpenseFilter(UnspecifiedPayment: true).ApplyTo(owned).CountAsync() == 1, "Unspecified filter failed");
    Check(new ExpenseSearchInput { PaymentMethod = "none" }.TryCreate(out var unspecified, out _) && unspecified.UnspecifiedPayment, "Filter parser failed");
    Check(!new ExpenseSearchInput { PaymentMethod = "-1" }.TryCreate(out _, out _), "Invalid filter accepted");
    var totals = await service.MonthlyTotalsAsync("A", new(2026, 10, 1));
    Check(totals.Sum(t => t.Amount) == 5800 && totals.Single(t => t.Id == cash.Id).Amount == 4800, "Monthly totals failed");
    Check((await service.MonthlyTotalsAsync("B", new(2026, 10, 1))).Sum(t => t.Amount) == 0, "Totals leaked owners");
    await service.SaveAsync("A", cash.Id, "현금, \"지갑\"", "현금");
    var exported = await owned.Include(e => e.PaymentMethod).ToListAsync();
    using var reader = new StreamReader(new MemoryStream(ExpenseCsvExporter.Create(exported)));
    var parsed = ExpenseCsvImporter.Parse(reader);
    Check(parsed.Issues.Count == 0 && parsed.Rows.Count == 2, "CSV roundtrip parse failed");
    var importer = new ExpenseCsvImportService(factory);
    Check((await importer.PreviewAsync("A", parsed.Rows)).All(c => c.IsDuplicate), "CSV payment duplicate detection failed");
    Check(await importer.ImportAsync("A", parsed.Rows, false) == 0, "CSV duplicated existing payment expense");
    await Reject(() => importer.ImportAsync("B", parsed.Rows, false));
    Check(await db.Expenses.CountAsync() == 2, "Unknown CSV method caused partial write");
    var old = ExpenseCsvImporter.Parse(new StringReader("날짜,금액(원),카테고리,메모\n2026-10-02,1200,교통,old"));
    Check(old.Issues.Count == 0 && await importer.ImportAsync("A", old.Rows, false) == 1, "Legacy 4-column CSV failed");
    Check((await db.Expenses.AsNoTracking().SingleAsync(e => e.Memo == "old")).PaymentMethodId is null, "Legacy CSV payment not null");
    var beforeDelete = await db.Expenses.CountAsync();
    await service.DeleteAsync("A", cash.Id);
    Check(await db.Expenses.CountAsync() == beforeDelete && (await db.Expenses.AsNoTracking().SingleAsync(e => e.Id == expense.Id)).PaymentMethodId is null, "Delete damaged expense");
    Check((await templates.FindAsync("A", template.Id))?.PaymentMethodId is null, "Delete did not unlink template");
    SetField("amount", 100L); SetField("paymentMethodId", cash.Id);
    await Invoke("AddExpenseAsync");
    Check(await db.Expenses.CountAsync() == beforeDelete, "Stale method accepted");
    await new UserDataDeletionService(factory).DeleteAsync("A");
    Check((await service.ListAsync("A")).Count == 0 && (await service.ListAsync("B")).Count == 1, "Account deletion isolation failed");
    // 신규·기존 스키마 모두 직접 SQL로 타 계정 연결도 거부합니다.
    try
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Expenses (OwnerId,Date,Amount,Category,Memo,PaymentMethodId) VALUES ('A','2026-10-01',1,'기타','bad',{foreign.Id})");
        throw new Exception("Database accepted cross-owner association");
    }
    catch (SqliteException) { }
    Console.WriteLine($"PASS ({(legacy ? "upgraded" : "new")} DB): CRUD, expense add/edit, templates, ownership, filters, monthly totals, CSV compatibility, unlink and account deletion");
}
