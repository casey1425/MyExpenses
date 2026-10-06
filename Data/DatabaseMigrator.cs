using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

// 앱 시작 시 DB를 최신 스키마로 맞춥니다.
//  - 새 DB: 마이그레이션을 처음부터 적용합니다.
//  - 마이그레이션 도입 이전에 만들어진 DB(EnsureCreated + ExpensesSchema 방식): 기존 방식으로 최신 상태까지 올린 뒤
//    InitialCreate를 "이미 적용됨"으로 기록(기준선)하고, 이후 마이그레이션만 적용합니다. 기존 데이터는 변경하지 않습니다.
//  - 이미 마이그레이션을 쓰는 DB: 아직 적용하지 않은 마이그레이션만 적용합니다.
// 스키마를 바꾸려면 ExpensesSchema가 아니라 `dotnet ef migrations add`로 마이그레이션을 추가하세요.
public static class DatabaseMigrator
{
    private const string HistoryTable = "__EFMigrationsHistory";
    private const string LockTable = "__EFMigrationsLock"; // EF Core가 동시 마이그레이션을 막기 위해 만드는 테이블

    public static Task MigrateExpensesAsync(ExpensesDbContext db, CancellationToken cancellationToken = default) =>
        MigrateAsync(db, () => ExpensesSchema.EnsureCreatedAsync(db, cancellationToken), cancellationToken);

    // 로그인 DB는 마이그레이션 도입 전 스키마 변경이 없었으므로 별도 보정 없이 기준선만 기록합니다.
    public static Task MigrateAuthAsync(AuthDbContext db, CancellationToken cancellationToken = default) =>
        MigrateAsync(db, null, cancellationToken);

    // 시작할 때 이 DB의 스키마가 바뀌는지(= 마이그레이션 이전 DB이거나 아직 적용하지 않은 마이그레이션이 있는지) 알려 줍니다.
    // 새 DB(사용자 테이블이 없음)나 이미 최신인 DB는 false입니다. 바뀌기 전에 백업할지 판단하는 데 씁니다.
    public static Task<bool> NeedsMigrationAsync(ExpensesDbContext db, CancellationToken cancellationToken = default) => NeedsAsync(db, cancellationToken);

    public static Task<bool> NeedsMigrationAsync(AuthDbContext db, CancellationToken cancellationToken = default) => NeedsAsync(db, cancellationToken);

    private static async Task<bool> NeedsAsync(DbContext db, CancellationToken cancellationToken)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Count == 0) return await HasUserTablesAsync(db, cancellationToken);
        return (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
    }

    private static async Task MigrateAsync(DbContext db, Func<Task>? upgradeLegacySchema, CancellationToken cancellationToken)
    {
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count == 0)
            throw new InvalidOperationException($"{db.GetType().Name}에 마이그레이션이 없습니다.");

        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (!applied.Any() && await HasUserTablesAsync(db, cancellationToken))
        {
            if (upgradeLegacySchema is not null)
                await upgradeLegacySchema();
            await BaselineAsync(db, migrations[0], cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    // 마이그레이션 기록은 없지만 사용자 테이블이 이미 있는 DB(= 마이그레이션 도입 이전 DB)인지 확인합니다.
    // EF가 관리하는 기록·잠금 테이블만 있는 DB는 새 DB로 취급합니다.
    private static async Task<bool> HasUserTablesAsync(DbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('{HistoryTable}', '{LockTable}');";
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // 스키마를 다시 만들지 않고 첫 마이그레이션만 "적용됨"으로 기록합니다.
    private static async Task BaselineAsync(DbContext db, string migrationId, CancellationToken cancellationToken)
    {
        var productVersion = typeof(DbContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0] ?? "unknown";
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE IF NOT EXISTS \"{HistoryTable}\" (\"MigrationId\" TEXT NOT NULL CONSTRAINT \"PK_{HistoryTable}\" PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);",
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {productVersion});",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
