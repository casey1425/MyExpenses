using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    await ExpensesSchema.EnsureCreatedAsync(db);
    await ExpensesSchema.EnsureCreatedAsync(db);

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
    Console.WriteLine($"PASS ({(legacy ? "upgraded" : "new")} DB): CRUD, validation, ownership, month boundaries, cashflow, savings rate and account deletion");
}
