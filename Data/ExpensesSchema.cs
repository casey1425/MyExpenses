using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public static class ExpensesSchema
{
    public static async Task EnsureCreatedAsync(ExpensesDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await ExecuteAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS "UserCategories" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Position" INTEGER NOT NULL,
                    "IsArchived" INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserCategories_OwnerId_Name"
                    ON "UserCategories" ("OwnerId", "Name");
                """, cancellationToken);
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
                CREATE TABLE IF NOT EXISTS "RecurringExpenseRules" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_RecurringExpenseRules" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "StartMonth" TEXT NOT NULL,
                    "DayOfMonth" INTEGER NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Memo" TEXT NOT NULL,
                    "IsActive" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_RecurringExpenseRules_OwnerId"
                    ON "RecurringExpenseRules" ("OwnerId");
                CREATE TABLE IF NOT EXISTS "RecurringExpenseOccurrences" (
                    "RuleId" INTEGER NOT NULL,
                    "Month" TEXT NOT NULL,
                    "OwnerId" TEXT NOT NULL,
                    CONSTRAINT "PK_RecurringExpenseOccurrences" PRIMARY KEY ("RuleId", "Month")
                );
                CREATE INDEX IF NOT EXISTS "IX_RecurringExpenseOccurrences_OwnerId"
                    ON "RecurringExpenseOccurrences" ("OwnerId");
                CREATE TABLE IF NOT EXISTS "CategoryBudgets" (
                    "OwnerId" TEXT NOT NULL,
                    "Month" TEXT NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    CONSTRAINT "PK_CategoryBudgets" PRIMARY KEY ("OwnerId", "Month", "Category")
                );
                CREATE TABLE IF NOT EXISTS "ExpenseTemplates" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ExpenseTemplates" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Category" TEXT NOT NULL,
                    "Memo" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_ExpenseTemplates_OwnerId" ON "ExpenseTemplates" ("OwnerId");
                """, cancellationToken);

            await ExecuteAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS "PaymentMethods" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PaymentMethods" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Type" TEXT NOT NULL,
                    CONSTRAINT "AK_PaymentMethods_OwnerId_Id" UNIQUE ("OwnerId", "Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_PaymentMethods_OwnerId_Name" ON "PaymentMethods" ("OwnerId", "Name");
                """, cancellationToken);
            foreach (var table in new[] { "Expenses", "ExpenseTemplates" })
            {
                if (!await HasColumnAsync(connection, transaction, table, "PaymentMethodId", cancellationToken))
                    await ExecuteAsync(connection, transaction, $"ALTER TABLE \"{table}\" ADD COLUMN \"PaymentMethodId\" INTEGER NULL;", cancellationToken);
                await ExecuteAsync(connection, transaction, $"CREATE INDEX IF NOT EXISTS \"IX_{table}_OwnerId_PaymentMethodId\" ON \"{table}\" (\"OwnerId\", \"PaymentMethodId\");", cancellationToken);
            }
            // 기존 테이블을 재작성하지 않고, 신규 DB의 복합 외래 키와 같은 소유자 검증을 적용합니다.
            foreach (var table in new[] { "Expenses", "ExpenseTemplates" })
            {
                foreach (var operation in new[] { "INSERT", "UPDATE" })
                    await ExecuteAsync(connection, transaction, $"""
                        CREATE TRIGGER IF NOT EXISTS "TR_{table}_PaymentOwner_{operation}"
                        BEFORE {operation} ON "{table}"
                        WHEN NEW."PaymentMethodId" IS NOT NULL AND NOT EXISTS (
                            SELECT 1 FROM "PaymentMethods" WHERE "Id" = NEW."PaymentMethodId" AND "OwnerId" = NEW."OwnerId")
                        BEGIN SELECT RAISE(ABORT, 'Invalid payment method owner'); END;
                        """, cancellationToken);
            }
            await ExecuteAsync(connection, transaction, """
                CREATE TRIGGER IF NOT EXISTS "TR_PaymentMethods_Referenced_DELETE"
                BEFORE DELETE ON "PaymentMethods"
                WHEN EXISTS (SELECT 1 FROM "Expenses" WHERE "OwnerId" = OLD."OwnerId" AND "PaymentMethodId" = OLD."Id")
                  OR EXISTS (SELECT 1 FROM "ExpenseTemplates" WHERE "OwnerId" = OLD."OwnerId" AND "PaymentMethodId" = OLD."Id")
                BEGIN SELECT RAISE(ABORT, 'Payment method still referenced'); END;
                """, cancellationToken);
            await ExecuteAsync(connection, transaction,
                """
                CREATE TABLE IF NOT EXISTS "Incomes" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Incomes" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "Date" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Source" TEXT NOT NULL,
                    "Memo" TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_Incomes_OwnerId_Date" ON "Incomes" ("OwnerId", "Date");
                CREATE TABLE IF NOT EXISTS "SavingsGoals" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_SavingsGoals" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "TargetAmount" INTEGER NOT NULL,
                    "TargetDate" TEXT NULL,
                    "CreatedDate" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SavingsGoals_OwnerId_Name" ON "SavingsGoals" ("OwnerId", "Name");
                CREATE TABLE IF NOT EXISTS "SavingsDeposits" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_SavingsDeposits" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "GoalId" INTEGER NOT NULL,
                    "Date" TEXT NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Memo" TEXT NOT NULL,
                    CONSTRAINT "FK_SavingsDeposits_SavingsGoals_GoalId" FOREIGN KEY ("GoalId") REFERENCES "SavingsGoals" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_SavingsDeposits_OwnerId_GoalId" ON "SavingsDeposits" ("OwnerId", "GoalId");
                CREATE TABLE IF NOT EXISTS "RecurringIncomeRules" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_RecurringIncomeRules" PRIMARY KEY AUTOINCREMENT,
                    "OwnerId" TEXT NOT NULL,
                    "StartMonth" TEXT NOT NULL,
                    "DayOfMonth" INTEGER NOT NULL,
                    "Amount" INTEGER NOT NULL,
                    "Source" TEXT NOT NULL,
                    "Memo" TEXT NOT NULL,
                    "IsActive" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_RecurringIncomeRules_OwnerId" ON "RecurringIncomeRules" ("OwnerId");
                CREATE TABLE IF NOT EXISTS "RecurringIncomeOccurrences" (
                    "RuleId" INTEGER NOT NULL,
                    "Month" TEXT NOT NULL,
                    "OwnerId" TEXT NOT NULL,
                    CONSTRAINT "PK_RecurringIncomeOccurrences" PRIMARY KEY ("RuleId", "Month")
                );
                CREATE INDEX IF NOT EXISTS "IX_RecurringIncomeOccurrences_OwnerId" ON "RecurringIncomeOccurrences" ("OwnerId");
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
