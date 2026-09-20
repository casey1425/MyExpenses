using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public static class BudgetSchema
{
    public static Task EnsureCreatedAsync(ExpensesDbContext db, CancellationToken cancellationToken = default)
    {
        // Existing installations used EnsureCreated, which does not add new tables to an existing database.
        // Create only the new table so previously saved expenses remain untouched.
        return db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MonthlyBudgets" (
                "Month" TEXT NOT NULL CONSTRAINT "PK_MonthlyBudgets" PRIMARY KEY,
                "Amount" INTEGER NOT NULL
            );
            """, cancellationToken);
    }
}
