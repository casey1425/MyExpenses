using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public static class BudgetSchema
{
    public static async Task EnsureCreatedAsync(ExpensesDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            if (!await HasColumnAsync(connection, transaction, "Expenses", "OwnerId", cancellationToken))
            {
                await ExecuteAsync(connection, transaction,
                    "ALTER TABLE \"Expenses\" ADD COLUMN \"OwnerId\" TEXT NOT NULL DEFAULT '';",
                    cancellationToken);
            }

            if (!await TableExistsAsync(connection, transaction, "MonthlyBudgets", cancellationToken))
            {
                await ExecuteAsync(connection, transaction,
                    """
                    CREATE TABLE "MonthlyBudgets" (
                        "OwnerId" TEXT NOT NULL,
                        "Month" TEXT NOT NULL,
                        "Amount" INTEGER NOT NULL,
                        CONSTRAINT "PK_MonthlyBudgets" PRIMARY KEY ("OwnerId", "Month")
                    );
                    """, cancellationToken);
            }
            else if (!await HasColumnAsync(connection, transaction, "MonthlyBudgets", "OwnerId", cancellationToken))
            {
                await ExecuteAsync(connection, transaction,
                    """
                    DROP TABLE IF EXISTS "MonthlyBudgets_new";
                    CREATE TABLE "MonthlyBudgets_new" (
                        "OwnerId" TEXT NOT NULL,
                        "Month" TEXT NOT NULL,
                        "Amount" INTEGER NOT NULL,
                        CONSTRAINT "PK_MonthlyBudgets" PRIMARY KEY ("OwnerId", "Month")
                    );
                    INSERT INTO "MonthlyBudgets_new" ("OwnerId", "Month", "Amount")
                        SELECT '', "Month", "Amount" FROM "MonthlyBudgets";
                    DROP TABLE "MonthlyBudgets";
                    ALTER TABLE "MonthlyBudgets_new" RENAME TO "MonthlyBudgets";
                    """, cancellationToken);
            }

            await ExecuteAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS "UserProfiles" (
                    "OwnerId" TEXT NOT NULL CONSTRAINT "PK_UserProfiles" PRIMARY KEY,
                    "HasCompletedOnboarding" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_Expenses_OwnerId_Date"
                    ON "Expenses" ("OwnerId", "Date");
                """, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasColumnAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
