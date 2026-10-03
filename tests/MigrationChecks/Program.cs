using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyExpenses.Data;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

static DbContextOptions<ExpensesDbContext> ExpensesOptions(SqliteConnection connection) =>
    new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options;

// 앱(DI)과 같은 Identity 기본 설정으로 모델을 만듭니다.
static DbContextOptions<AuthDbContext> AuthOptions(SqliteConnection connection)
{
    var services = new ServiceCollection();
    services.AddIdentityCore<IdentityUser>();
    return new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection)
        .UseApplicationServiceProvider(services.BuildServiceProvider()).Options;
}

static async Task<SqliteConnection> OpenAsync()
{
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    return connection;
}

static async Task<List<string>> QueryAsync(DbConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<string>();
    while (await reader.ReadAsync())
        rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString())));
    return rows;
}

static async Task<List<string>> TablesAsync(DbConnection connection) => await QueryAsync(connection,
    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' ORDER BY name");

// 테이블별 열·인덱스·외래 키를 비교 가능한 문자열로 만듭니다(열 순서와 자동 생성 이름에는 의존하지 않음).
static async Task<SortedDictionary<string, string>> SchemaAsync(DbConnection connection)
{
    var result = new SortedDictionary<string, string>();
    foreach (var table in await TablesAsync(connection))
    {
        var columns = await QueryAsync(connection, $"SELECT name,type,\"notnull\",IFNULL(dflt_value,''),pk FROM pragma_table_info('{table}') ORDER BY name");
        var indexes = (await QueryAsync(connection, $"SELECT name,\"unique\",origin FROM pragma_index_list('{table}') WHERE origin='c' ORDER BY name"));
        var indexColumns = new List<string>();
        foreach (var index in indexes)
            indexColumns.Add(index + ":" + string.Join(",", await QueryAsync(connection, $"SELECT name FROM pragma_index_info('{index.Split('|')[0]}') ORDER BY seqno")));
        var keys = await QueryAsync(connection, $"SELECT \"table\",\"from\",\"to\",on_update,on_delete FROM pragma_foreign_key_list('{table}') ORDER BY \"table\",\"from\"");
        result[table] = $"COLUMNS[{string.Join(";", columns)}] INDEXES[{string.Join(";", indexColumns)}] FKS[{string.Join(";", keys)}]";
    }
    return result;
}

static async Task<Dictionary<string, long>> CountsAsync(DbConnection connection)
{
    var counts = new Dictionary<string, long>();
    foreach (var table in await TablesAsync(connection))
        counts[table] = long.Parse((await QueryAsync(connection, $"SELECT COUNT(*) FROM \"{table}\""))[0]);
    return counts;
}

static bool SameCounts(Dictionary<string, long> before, Dictionary<string, long> after) =>
    before.All(pair => after.TryGetValue(pair.Key, out var count) && count == pair.Value);

// ---- 1. 새 DB: 마이그레이션이 모델(EnsureCreated)과 같은 스키마를 만든다 ----
await using var migratedConnection = await OpenAsync();
await using var modelConnection = await OpenAsync();
{
    await using var migrated = new ExpensesDbContext(ExpensesOptions(migratedConnection));
    await DatabaseMigrator.MigrateExpensesAsync(migrated);
    await using var model = new ExpensesDbContext(ExpensesOptions(modelConnection));
    await model.Database.EnsureCreatedAsync();

    var allMigrations = migrated.Database.GetMigrations().ToList();
    Check((await migrated.Database.GetAppliedMigrationsAsync()).SequenceEqual(allMigrations) && !(await migrated.Database.GetPendingMigrationsAsync()).Any(), "Fresh DB did not apply all migrations");
    var migratedSchema = await SchemaAsync(migratedConnection);
    var modelSchema = await SchemaAsync(modelConnection);
    Check(migratedSchema.Count == 14 && migratedSchema.Keys.SequenceEqual(modelSchema.Keys), $"Table set differs: {string.Join(",", migratedSchema.Keys.Except(modelSchema.Keys))} / {string.Join(",", modelSchema.Keys.Except(migratedSchema.Keys))}");
    foreach (var (table, definition) in modelSchema)
        Check(migratedSchema[table] == definition, $"Schema differs from model for {table}:\n  migration: {migratedSchema[table]}\n  model:     {definition}");

    // 모델을 바꾸고 마이그레이션을 추가하지 않았다면 여기서 실패합니다(CI 보호 장치).
    Check(!migrated.Database.HasPendingModelChanges(), "ExpensesDbContext model changed without a migration. Run: dotnet ef migrations add <Name> --context ExpensesDbContext --output-dir Data/Migrations/Expenses");

    // 두 번째 시작은 아무것도 바꾸지 않는다.
    var before = await SchemaAsync(migratedConnection);
    await DatabaseMigrator.MigrateExpensesAsync(migrated);
    Check((await SchemaAsync(migratedConnection)).SequenceEqual(before) && (await migrated.Database.GetAppliedMigrationsAsync()).Count() == allMigrations.Count, "Second startup changed the database");

    // 외래 키가 실제로 동작한다: 다른 소유자 결제수단 연결 거부, 목표 삭제 시 저축 연쇄 삭제.
    migrated.PaymentMethods.Add(new PaymentMethod { OwnerId = "B", Name = "B카드", Type = "체크카드" });
    await migrated.SaveChangesAsync();
    var foreignMethod = migrated.PaymentMethods.Single().Id;
    try
    {
        await migrated.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Expenses (OwnerId,Date,Amount,Category,Memo,PaymentMethodId) VALUES ('A','2026-10-01',1,'기타','bad',{foreignMethod})");
        throw new Exception("Cross-owner payment method accepted");
    }
    catch (SqliteException) { }
    migrated.SavingsGoals.Add(new SavingsGoal { OwnerId = "A", Name = "g", TargetAmount = 1000, CreatedDate = new(2026, 10, 1) });
    await migrated.SaveChangesAsync();
    migrated.SavingsDeposits.Add(new SavingsDeposit { OwnerId = "A", GoalId = migrated.SavingsGoals.Single().Id, Date = new(2026, 10, 1), Amount = 10 });
    await migrated.SaveChangesAsync();
    await migrated.Database.ExecuteSqlRawAsync("DELETE FROM SavingsGoals");
    Check(await migrated.SavingsDeposits.CountAsync() == 0, "Cascade delete missing after migration");
}

// ---- 2. 마이그레이션 도입 이전 DB(EnsureCreated + ExpensesSchema): 데이터를 그대로 두고 기준선만 기록한다 ----
await using var legacyConnection = await OpenAsync();
{
    await using (var seed = new ExpensesDbContext(ExpensesOptions(legacyConnection)))
    {
        await seed.Database.EnsureCreatedAsync();
        await ExpensesSchema.EnsureCreatedAsync(seed);
        seed.PaymentMethods.Add(new PaymentMethod { OwnerId = "A", Name = "카드", Type = "체크카드" });
        seed.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new(2026, 9, 1), Amount = 12_000, Category = "식비", Memo = "점심" });
        seed.Incomes.Add(new IncomeRecord { OwnerId = "A", Date = new(2026, 9, 25), Amount = 3_000_000, Source = "급여", Memo = "월급" });
        seed.SavingsGoals.Add(new SavingsGoal { OwnerId = "A", Name = "여행", TargetAmount = 3_000_000, CreatedDate = new(2026, 9, 1) });
        await seed.SaveChangesAsync();
    }
    var before = await CountsAsync(legacyConnection);
    var schemaBefore = await SchemaAsync(legacyConnection);
    Check((await QueryAsync(legacyConnection, "SELECT name FROM sqlite_master WHERE name='__EFMigrationsHistory'")).Count == 0, "Legacy fixture should not have a history table");

    await using var db = new ExpensesDbContext(ExpensesOptions(legacyConnection));
    await DatabaseMigrator.MigrateExpensesAsync(db);
    var history = await QueryAsync(legacyConnection, "SELECT MigrationId FROM __EFMigrationsHistory");
    Check(history.SequenceEqual(db.Database.GetMigrations()), "Legacy DB was not baselined with the initial migration only");
    Check(SameCounts(before, await CountsAsync(legacyConnection)) && (await CountsAsync(legacyConnection)).Values.Sum() == before.Values.Sum(), "Legacy data changed during baseline");
    Check((await SchemaAsync(legacyConnection)).SequenceEqual(schemaBefore), "Baseline must not rebuild existing tables");
    Check((await db.Expenses.SingleAsync()).Memo == "점심" && (await db.Incomes.SingleAsync()).Amount == 3_000_000, "Legacy rows unreadable after baseline");
    await DatabaseMigrator.MigrateExpensesAsync(db);
    Check((await QueryAsync(legacyConnection, "SELECT COUNT(*) FROM __EFMigrationsHistory"))[0] == history.Count.ToString(), "Second startup re-baselined");
}

// ---- 3. 아주 오래된 DB(수입·결제수단·카테고리 이전)도 기존 업그레이드 후 기준선 ----
await using var ancientConnection = await OpenAsync();
{
    await using (var raw = new ExpensesDbContext(ExpensesOptions(ancientConnection)))
        await raw.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Expenses (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId TEXT NOT NULL, Date TEXT NOT NULL, Amount INTEGER NOT NULL, Category TEXT NOT NULL, Memo TEXT NOT NULL);
            CREATE TABLE ExpenseTemplates (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerId TEXT NOT NULL, Name TEXT NOT NULL, Amount INTEGER NOT NULL, Category TEXT NOT NULL, Memo TEXT NOT NULL);
            INSERT INTO Expenses (OwnerId,Date,Amount,Category,Memo) VALUES ('A','2026-10-01 00:00:00',1000,'교통','existing');
            INSERT INTO ExpenseTemplates (OwnerId,Name,Amount,Category,Memo) VALUES ('A','커피',4500,'카페','커피');
            """);
    await using var db = new ExpensesDbContext(ExpensesOptions(ancientConnection));
    await DatabaseMigrator.MigrateExpensesAsync(db);
    var tables = await TablesAsync(ancientConnection);
    Check(tables.Count == 14, $"Ancient DB missing tables after upgrade: {tables.Count}");
    Check((await db.Expenses.SingleAsync()) is { Memo: "existing", Amount: 1000 } && (await db.ExpenseTemplates.SingleAsync()).Name == "커피", "Ancient rows lost");
    Check((await QueryAsync(ancientConnection, "SELECT COUNT(*) FROM __EFMigrationsHistory"))[0] == "1", "Ancient DB not baselined");
    db.Incomes.Add(new IncomeRecord { OwnerId = "A", Date = new(2026, 10, 1), Amount = 5, Source = "급여", Memo = "" });
    await db.SaveChangesAsync();
}

// ---- 4. EF가 만든 잠금 테이블만 남은 DB는 새 DB로 취급한다 ----
await using var lockConnection = await OpenAsync();
{
    await using var db = new ExpensesDbContext(ExpensesOptions(lockConnection));
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE __EFMigrationsLock (Id INTEGER NOT NULL CONSTRAINT PK___EFMigrationsLock PRIMARY KEY, Timestamp TEXT NOT NULL);");
    await DatabaseMigrator.MigrateExpensesAsync(db);
    Check((await TablesAsync(lockConnection)).Count == 14 && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Lock-table-only DB not treated as fresh");
}

// ---- 5. 로그인(Identity) DB ----
await using var authFreshConnection = await OpenAsync();
await using var authModelConnection = await OpenAsync();
await using var authLegacyConnection = await OpenAsync();
{
    await using var fresh = new AuthDbContext(AuthOptions(authFreshConnection));
    await DatabaseMigrator.MigrateAuthAsync(fresh);
    await using var model = new AuthDbContext(AuthOptions(authModelConnection));
    await model.Database.EnsureCreatedAsync();
    var freshSchema = await SchemaAsync(authFreshConnection);
    var modelSchema = await SchemaAsync(authModelConnection);
    Check(freshSchema.Count == 7 && freshSchema.Keys.SequenceEqual(modelSchema.Keys), "Auth table set differs");
    foreach (var (table, definition) in modelSchema)
        Check(freshSchema[table] == definition, $"Auth schema differs from model for {table}");
    Check(!fresh.Database.HasPendingModelChanges(), "AuthDbContext model changed without a migration. Run: dotnet ef migrations add <Name> --context AuthDbContext --output-dir Data/Migrations/Auth");

    // 기존 로그인 DB: 사용자 데이터를 유지한 채 기준선만 기록
    await using (var seed = new AuthDbContext(AuthOptions(authLegacyConnection)))
    {
        await seed.Database.EnsureCreatedAsync();
        seed.Users.Add(new IdentityUser { Id = "u1", UserName = "me@example.com", NormalizedUserName = "ME@EXAMPLE.COM", Email = "me@example.com", NormalizedEmail = "ME@EXAMPLE.COM" });
        await seed.SaveChangesAsync();
    }
    await using var legacy = new AuthDbContext(AuthOptions(authLegacyConnection));
    await DatabaseMigrator.MigrateAuthAsync(legacy);
    await DatabaseMigrator.MigrateAuthAsync(legacy);
    Check((await legacy.Users.SingleAsync()).Email == "me@example.com", "Auth user lost during baseline");
    Check((await QueryAsync(authLegacyConnection, "SELECT COUNT(*) FROM __EFMigrationsHistory"))[0] == legacy.Database.GetMigrations().Count().ToString(), "Auth DB not baselined exactly once");
}

Console.WriteLine("PASS: fresh/legacy/ancient DB migration, baseline without data loss, idempotent startup, model-vs-migration schema equality, pending-model-change guard, Identity DB");
