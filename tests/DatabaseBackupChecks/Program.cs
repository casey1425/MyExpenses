using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Data;
using MyExpenses.Services.Backups;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

var root = Path.Combine(Path.GetTempPath(), "myexpenses-backup-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try { await RunAsync(root); }
finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(root, recursive: true); } catch { } }
Console.WriteLine("PASS: backup settings, schedule, online copy with integrity check, atomic failure, naming and listing, retention, worker loop, pre-migration detection and snapshot");

static async Task RunAsync(string root)
{
    // ── 설정 ──
    IConfiguration Config(params (string, string?)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();
    var data = Path.Combine(root, "data");
    var defaults = DatabaseBackupOptions.From(Config(), data);
    Check(defaults.Enabled && defaults.Interval == TimeSpan.FromHours(24) && defaults.Keep == 14 && defaults.KeepBeforeMigration == 3 && defaults.Directory == Path.Combine(data, "backups") && defaults.StartDelay == TimeSpan.FromSeconds(30) && defaults.Warnings.Count == 0, "Default options wrong");
    var custom = DatabaseBackupOptions.From(Config(("Backup:Enabled", "false"), ("Backup:IntervalHours", "6"), ("Backup:Keep", "30"), ("Backup:KeepBeforeMigration", "0"), ("Backup:Directory", "../elsewhere"), ("Backup:StartDelaySeconds", "5")), data);
    Check(!custom.Enabled && custom.Interval == TimeSpan.FromHours(6) && custom.Keep == 30 && custom.KeepBeforeMigration == 0 && custom.Directory == Path.GetFullPath("../elsewhere", data) && custom.StartDelay == TimeSpan.FromSeconds(5) && custom.Warnings.Count == 0, "Custom options wrong");
    Check(DatabaseBackupOptions.From(Config(("Backup:Directory", "/var/backups/x")), data).Directory == "/var/backups/x", "Absolute directory must be kept");
    var invalid = DatabaseBackupOptions.From(Config(("Backup:Enabled", "maybe"), ("Backup:IntervalHours", "0"), ("Backup:Keep", "-3"), ("Backup:KeepBeforeMigration", "99"), ("Backup:StartDelaySeconds", "abc")), data);
    Check(invalid.Enabled && invalid.Interval == TimeSpan.FromHours(24) && invalid.Keep == 14 && invalid.KeepBeforeMigration == 3 && invalid.StartDelay == TimeSpan.FromSeconds(30) && invalid.Warnings.Count == 5, $"Invalid options must fall back with warnings ({invalid.Warnings.Count})");
    Check(DatabaseBackupOptions.From(Config(("Backup:IntervalHours", "99999")), data).Interval == TimeSpan.FromHours(24), "Huge interval accepted");
    Check(DatabaseBackupOptions.From(Config(("Backup:Keep", "0")), data) is { Keep: 14, Warnings.Count: 1 }, "Keep=0 would delete every backup and must be rejected");
    Check(DatabaseBackupOptions.From(Config(("Backup:KeepBeforeMigration", "0")), data) is { KeepBeforeMigration: 0, Warnings.Count: 0 }, "KeepBeforeMigration=0 is allowed");

    // ── 다음 백업까지의 시간 ──
    var t0 = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    var day = TimeSpan.FromHours(24);
    Check(DatabaseBackupService.NextDelay(null, t0, day) == TimeSpan.Zero, "No backup yet must run now");
    Check(DatabaseBackupService.NextDelay(t0.AddHours(-1), t0, day) == TimeSpan.FromHours(23), "Delay after 1h wrong");
    Check(DatabaseBackupService.NextDelay(t0.AddHours(-24), t0, day) == TimeSpan.Zero && DatabaseBackupService.NextDelay(t0.AddDays(-40), t0, day) == TimeSpan.Zero, "Overdue must run now");
    Check(DatabaseBackupService.NextDelay(t0.AddHours(1), t0, day) == day, "A backup from the future (clock skew) must wait a full interval");
    Check(DatabaseBackupService.NextDelay(t0.AddHours(-24).AddSeconds(1), t0, day) == TimeSpan.FromSeconds(1), "Boundary wrong");

    // ── 원본 DB 만들기 ──
    var live = Path.Combine(root, "live"); Directory.CreateDirectory(live);
    var expensesPath = Path.Combine(live, "myexpenses.db");
    var authPath = Path.Combine(live, "auth.db");
    await using (var db = NewExpenses(expensesPath))
    {
        await DatabaseMigrator.MigrateExpensesAsync(db);
        for (var i = 0; i < 300; i++) db.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new DateTime(2026, 10, 1), Amount = 100 + i, Category = "식비", Memo = $"기록{i}" });
        await db.SaveChangesAsync();
    }
    await using (var auth = NewAuth(authPath))
    {
        await DatabaseMigrator.MigrateAuthAsync(auth);
        auth.Users.Add(new IdentityUser { UserName = "me@example.com", NormalizedUserName = "ME@EXAMPLE.COM", Email = "me@example.com", NormalizedEmail = "ME@EXAMPLE.COM", SecurityStamp = "x", ConcurrencyStamp = "y", Id = "u1" });
        await auth.SaveChangesAsync();
    }

    // ── 백업 만들기 ──
    var clockValue = new DateTimeOffset(2026, 10, 6, 3, 0, 0, TimeSpan.Zero);   // KST 12:00
    DatabaseBackupService NewService(string backupDirectory, int keep = 14, int keepPre = 3, params string[] files) =>
        new(new DatabaseBackupOptions(true, TimeSpan.FromHours(24), keep, keepPre, backupDirectory, TimeSpan.Zero, []), files.Length == 0 ? [expensesPath, authPath] : files,
            NullLogger<DatabaseBackupService>.Instance, () => clockValue);
    var backupDir = Path.Combine(root, "backups");
    var service = NewService(backupDir);
    var first = await service.CreateAsync();
    Check(first.Name == "20261006-120000" && !first.BeforeMigration && first.Files.SequenceEqual(new[] { "auth.db", "myexpenses.db" }) && first.Bytes > 0 && first.CreatedAt == new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9)), $"Backup info wrong: {first.Name}");
    Check(Directory.GetDirectories(backupDir).Length == 1 && !Directory.GetDirectories(backupDir, ".tmp-*").Any(), "Only the finished folder may remain");
    if (!OperatingSystem.IsWindows())
        Check((File.GetUnixFileMode(backupDir) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0, "Backup folder must be private");
    long CountExpenses(string file) { using var c = Open(file); return Convert.ToInt64(Scalar(c, "SELECT COUNT(*) FROM Expenses")); }
    Check(CountExpenses(Path.Combine(backupDir, first.Name, "myexpenses.db")) == 300, "Backup copy has wrong rows");
    using (var c = Open(Path.Combine(backupDir, first.Name, "auth.db")))
        Check(Convert.ToString(Scalar(c, "SELECT Email FROM AspNetUsers")) == "me@example.com", "Auth backup has wrong rows");
    // 이후 변경은 이미 만든 백업에 영향이 없음
    await using (var db = NewExpenses(expensesPath)) { db.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new DateTime(2026, 10, 2), Amount = 1, Category = "식비", Memo = "백업 이후" }); await db.SaveChangesAsync(); }
    Check(CountExpenses(Path.Combine(backupDir, first.Name, "myexpenses.db")) == 300 && CountExpenses(expensesPath) == 301, "A backup must be a snapshot");

    // 같은 시각에 여러 번 만들면 이름이 겹치지 않음
    var second = await service.CreateAsync(); var third = await service.CreateAsync();
    Check(new[] { first.Name, second.Name, third.Name }.Distinct().Count() == 3 && second.Name == "20261006-120001" && third.Name == "20261006-120002", $"Same-second names wrong: {second.Name}, {third.Name}");
    Check(CountExpenses(Path.Combine(backupDir, third.Name, "myexpenses.db")) == 301, "Later backup must contain later data");
    Check(service.List().Select(i => i.Name).SequenceEqual(new[] { third.Name, second.Name, first.Name }), "List must be newest first");

    // 동시에 호출해도 순서대로 처리
    var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.CreateAsync()));
    Check(parallel.Select(p => p.Name).Distinct().Count() == 4 && service.List().Count == 7, "Parallel backups must all succeed with distinct names");

    // 쓰는 도중에도 일관된 백업
    using var stop = new CancellationTokenSource();
    var writer = Task.Run(async () =>
    {
        var n = 0;
        while (!stop.IsCancellationRequested)
        {
            await using var db = NewExpenses(expensesPath);
            db.Expenses.Add(new ExpenseRecord { OwnerId = "W", Date = new DateTime(2026, 10, 3), Amount = ++n, Category = "식비", Memo = "동시 기록" });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { }
            await Task.Delay(2);
        }
    });
    var concurrent = new List<string>();
    for (var i = 0; i < 4; i++) concurrent.Add((await service.CreateAsync()).Name);
    stop.Cancel(); await writer;
    foreach (var name in concurrent)
    {
        using var c = Open(Path.Combine(backupDir, name, "myexpenses.db"));
        Check(Convert.ToString(Scalar(c, "PRAGMA integrity_check")) == "ok" && Convert.ToInt64(Scalar(c, "SELECT COUNT(*) FROM Expenses")) >= 301, $"Backup during writes is not consistent: {name}");
    }

    // ── 실패하면 아무것도 남기지 않음 ──
    var brokenAuth = Path.Combine(root, "broken-auth.db");
    File.WriteAllText(brokenAuth, "이것은 SQLite 파일이 아닙니다. " + new string('x', 5000));
    var failDir = Path.Combine(root, "fail-backups");
    var failing = NewService(failDir, 14, 3, expensesPath, brokenAuth);
    try { await failing.CreateAsync(); throw new Exception("Backup of a broken database succeeded"); } catch (SqliteException) { }
    Check(failing.List().Count == 0 && !Directory.GetDirectories(failDir).Any(), "A failed backup left a folder behind");
    var onlyMissing = NewService(Path.Combine(root, "none-backups"), 14, 3, Path.Combine(root, "missing.db"));
    try { await onlyMissing.CreateAsync(); throw new Exception("Backup without a database succeeded"); } catch (InvalidOperationException) { }
    Check(!Directory.Exists(Path.Combine(root, "none-backups")) || !Directory.GetDirectories(Path.Combine(root, "none-backups")).Any(), "Folder created without anything to back up");
    // 앱이 쓰는 도중의 "database is locked"(BUSY/LOCKED)는 잠깐 기다렸다 다시 시도하고, 그 밖의 오류는 다시 시도하지 않음
    var attemptsMade = 0;
    DatabaseBackupService LockedService(string dir, int attempts, Func<int, Exception?> failure) =>
        new(new DatabaseBackupOptions(true, TimeSpan.FromHours(24), 14, 3, dir, TimeSpan.Zero, []), [expensesPath], NullLogger<DatabaseBackupService>.Instance, () => clockValue,
            attempts, TimeSpan.FromMilliseconds(1), (from, to) =>
            {
                attemptsMade++;
                if (failure(attemptsMade) is Exception problem) throw problem;
                File.Copy(from, to, true);
            });
    var transientDir = Path.Combine(root, "transient-backups");
    var transient = await LockedService(transientDir, 5, n => n <= 3 ? new SqliteException("database is locked", n % 2 == 0 ? 6 : 5) : null).CreateAsync();
    Check(attemptsMade == 4 && File.Exists(Path.Combine(transientDir, transient.Name, "myexpenses.db")), $"Locked database must be retried until it works (attempts {attemptsMade})");
    attemptsMade = 0;
    var alwaysLockedDir = Path.Combine(root, "locked-backups");
    try { await LockedService(alwaysLockedDir, 3, _ => new SqliteException("database is locked", 5)).CreateAsync(); throw new Exception("A permanently locked database produced a backup"); }
    catch (SqliteException) { }
    Check(attemptsMade == 3 && (!Directory.Exists(alwaysLockedDir) || !Directory.GetDirectories(alwaysLockedDir).Any()), $"Retries must be bounded and leave nothing behind (attempts {attemptsMade})");
    attemptsMade = 0;
    try { await LockedService(Path.Combine(root, "other-error-backups"), 5, _ => new SqliteException("disk I/O error", 10)).CreateAsync(); throw new Exception("Other SQLite errors must not be swallowed"); }
    catch (SqliteException) { }
    Check(attemptsMade == 1, $"Errors other than locking must not be retried (attempts {attemptsMade})");

    // 읽기는 되지만 내용이 손상된 DB: 복사는 되더라도 점검에서 걸러 완성된 백업으로 인정하지 않음
    var corruptPath = Path.Combine(root, "corrupt.db");
    SqliteConnection.ClearAllPools();
    File.Copy(expensesPath, corruptPath, true);
    using (var stream = new FileStream(corruptPath, FileMode.Open, FileAccess.Write))
    {
        foreach (var page in new[] { 1, 2, 3, 4, 5 })
        {
            stream.Seek(page * 4096L, SeekOrigin.Begin);
            stream.Write(Enumerable.Repeat((byte)0xFF, 4096).ToArray());
        }
    }
    var corruptDir = Path.Combine(root, "corrupt-backups");
    try { await NewService(corruptDir, 14, 3, corruptPath).CreateAsync(); throw new Exception("A corrupted database was accepted as a backup"); }
    catch (SqliteException) { }
    catch (InvalidOperationException ex) when (ex.Message.Contains("점검에 실패")) { }
    Check(!Directory.Exists(corruptDir) || !Directory.GetDirectories(corruptDir).Any(), "A rejected corrupt backup left a folder behind");
    // 일부 파일이 없으면(아직 만들어지지 않은 로그인 DB 등) 있는 것만 백업
    var partial = await NewService(Path.Combine(root, "partial-backups"), 14, 3, expensesPath, Path.Combine(root, "missing.db")).CreateAsync();
    Check(partial.Files.SequenceEqual(new[] { "myexpenses.db" }), "Missing optional database must be skipped");
    // 취소
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    try { await NewService(Path.Combine(root, "cancel-backups")).CreateAsync(cancellationToken: cancelled.Token); throw new Exception("Cancelled backup completed"); } catch (OperationCanceledException) { }
    Check(!Directory.Exists(Path.Combine(root, "cancel-backups")) || !Directory.GetDirectories(Path.Combine(root, "cancel-backups")).Any(), "Cancelled backup left a folder behind");

    // ── 목록: 우리 형식이 아닌 것은 무시, 심볼릭 링크는 따라가지 않음 ──
    var listDir = Path.Combine(root, "list-backups"); Directory.CreateDirectory(listDir);
    var listService = NewService(listDir, 2, 1);
    foreach (var foreign in new[] { "notes", "20261006", "2026-10-06", "20261006-120000-extra", "20261006-250000", "20261340-120000", "backup-20261006-120000" })
        Directory.CreateDirectory(Path.Combine(listDir, foreign));
    File.WriteAllText(Path.Combine(listDir, "20261001-000000"), "this is a file, not a folder");
    Check(listService.List().Count == 0, "Foreign entries must not be listed: " + string.Join(",", listService.List().Select(i => i.Name)));
    var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "precious.txt"), "keep");
    if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(listDir, "20250101-000000"), outside);
    Check(listService.List().Count == 0, "A symlinked folder must not count as a backup");

    // ── 보관 정책 ──
    var retentionDir = Path.Combine(root, "retention-backups");
    var retention = NewService(retentionDir, keep: 3, keepPre: 2);
    for (var i = 0; i < 6; i++) { clockValue = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero).AddDays(i); await retention.CreateAsync(); }
    for (var i = 0; i < 4; i++) { clockValue = new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero).AddHours(i); await retention.CreateAsync(beforeMigration: true); }
    Directory.CreateDirectory(Path.Combine(retentionDir, "my-notes")); File.WriteAllText(Path.Combine(retentionDir, "my-notes", "a.txt"), "keep");
    Directory.CreateDirectory(Path.Combine(retentionDir, "20200101-000000-manual"));   // 사용자가 만든 이름
    Check(retention.List().Count == 10, "Setup: 10 backups expected");
    var deleted = retention.Prune();
    var remaining = retention.List();
    Check(deleted == 5 && remaining.Count(i => !i.BeforeMigration) == 3 && remaining.Count(i => i.BeforeMigration) == 2, $"Retention counts wrong: deleted {deleted}");
    Check(remaining.Where(i => !i.BeforeMigration).Select(i => i.Name).SequenceEqual(new[] { "20261006-120000", "20261005-120000", "20261004-120000" }), "Retention must keep the newest scheduled backups: " + string.Join(",", remaining.Select(i => i.Name)));
    Check(remaining.Where(i => i.BeforeMigration).Select(i => i.Name).SequenceEqual(new[] { "20261010-150000-premigrate", "20261010-140000-premigrate" }), "Retention must keep the newest pre-migration backups");
    Check(Directory.Exists(Path.Combine(retentionDir, "my-notes")) && Directory.Exists(Path.Combine(retentionDir, "20200101-000000-manual")), "Prune deleted a folder it does not own");
    Check(retention.Prune() == 0, "Second prune must do nothing");
    if (!OperatingSystem.IsWindows())
    {
        var linkDir = Path.Combine(retentionDir, "20190101-000000");
        Directory.CreateSymbolicLink(linkDir, outside);
        listService = NewService(retentionDir, 1, 1);
        listService.Prune();
        Check(File.Exists(Path.Combine(outside, "precious.txt")) && Directory.Exists(linkDir), "Prune followed a symlink");
        Directory.Delete(linkDir);
    }
    // 오래 남은 임시 폴더만 정리
    var oldTemp = Path.Combine(retentionDir, ".tmp-20260101-000000"); var freshTemp = Path.Combine(retentionDir, ".tmp-20261006-120000");
    Directory.CreateDirectory(oldTemp); Directory.CreateDirectory(freshTemp);
    Directory.SetLastWriteTimeUtc(oldTemp, clockValue.UtcDateTime.AddHours(-3)); Directory.SetLastWriteTimeUtc(freshTemp, clockValue.UtcDateTime.AddMinutes(-5));
    retention.Prune();
    Check(!Directory.Exists(oldTemp) && Directory.Exists(freshTemp), "Stale temp cleanup wrong");
    if (!OperatingSystem.IsWindows())
    {
        // 오래된 임시 이름의 심볼릭 링크라도 링크 대상 안의 파일은 지우지 않음
        Directory.SetLastWriteTimeUtc(outside, clockValue.UtcDateTime.AddDays(-3));
        var tempLink = Path.Combine(retentionDir, ".tmp-20190101-000000");
        Directory.CreateSymbolicLink(tempLink, outside);
        retention.Prune();
        Check(File.Exists(Path.Combine(outside, "precious.txt")), "Prune deleted files through a symlink");
        if (Directory.Exists(tempLink)) Directory.Delete(tempLink);
    }
    // 항상 1개는 남김(설정이 1이어도)
    var one = NewService(Path.Combine(root, "one-backups"), keep: 1, keepPre: 0);
    for (var i = 0; i < 3; i++) { clockValue = clockValue.AddDays(1); await one.CreateAsync(); await one.CreateAsync(beforeMigration: true); }
    one.Prune();
    Check(one.List().Count(i => !i.BeforeMigration) == 1 && one.List().Count(i => i.BeforeMigration) == 0 && one.List().Single().Name == "20261013-150000", "Keep=1 / KeepBeforeMigration=0 wrong");

    // ── 정기 작업 ──
    var workerDir = Path.Combine(root, "worker-backups");
    var workerClock = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
    var workerService = new DatabaseBackupService(new DatabaseBackupOptions(true, TimeSpan.FromHours(24), 2, 3, workerDir, TimeSpan.Zero, []), [expensesPath, authPath], NullLogger<DatabaseBackupService>.Instance, () => workerClock);
    var worker = new DatabaseBackupWorker(workerService, NullLogger<DatabaseBackupWorker>.Instance);
    Check(await worker.RunOnceAsync(workerClock, default) == TimeSpan.FromHours(24) && workerService.List().Count == 1, "Worker must back up when none exists");
    workerClock = workerClock.AddHours(5);
    Check(await worker.RunOnceAsync(workerClock, default) == TimeSpan.FromHours(19) && workerService.List().Count == 1, "Worker must wait until the interval has passed");
    workerClock = workerClock.AddHours(19);
    Check(await worker.RunOnceAsync(workerClock, default) == TimeSpan.FromHours(24) && workerService.List().Count == 2, "Worker must back up when due");
    workerClock = workerClock.AddMinutes(30);
    await workerService.CreateAsync(beforeMigration: true);   // 마이그레이션 전 백업은 정기 일정에 영향을 주지 않음
    workerClock = workerClock.AddMinutes(30);
    Check(await worker.RunOnceAsync(workerClock, default) == TimeSpan.FromHours(23), "Pre-migration backups must not reset the schedule");
    workerClock = workerClock.AddMinutes(0);
    for (var i = 0; i < 3; i++) { workerClock = workerClock.AddDays(1); await worker.RunOnceAsync(workerClock, default); }
    Check(workerService.List().Count(x => !x.BeforeMigration) == 2 && workerService.List().Count(x => x.BeforeMigration) == 1, "Worker must prune old backups");
    // 실패하면 한 시간 뒤에 다시, 예외는 밖으로 나가지 않음
    var failingWorker = new DatabaseBackupWorker(new DatabaseBackupService(new DatabaseBackupOptions(true, TimeSpan.FromHours(24), 2, 3, Path.Combine(root, "worker-fail"), TimeSpan.Zero, []), [brokenAuth], NullLogger<DatabaseBackupService>.Instance, () => workerClock), NullLogger<DatabaseBackupWorker>.Instance);
    Check(await failingWorker.RunOnceAsync(workerClock, default) == TimeSpan.FromHours(1), "Failed backup must retry in an hour");
    try { await worker.RunOnceAsync(workerClock.AddDays(10), cancelled.Token); throw new Exception("Cancellation swallowed"); } catch (OperationCanceledException) { }
    Check(workerService.LastScheduledAt() is DateTimeOffset last && last == workerService.List().First(x => !x.BeforeMigration).CreatedAt, "LastScheduledAt must ignore pre-migration backups");

    // ── 업그레이드 전 백업이 필요한지 판단 ──
    var needs = Path.Combine(root, "needs"); Directory.CreateDirectory(needs);
    // 새 DB(파일 없음·빈 파일)
    await using (var fresh = NewExpenses(Path.Combine(needs, "fresh.db"))) Check(!await DatabaseMigrator.NeedsMigrationAsync(fresh), "A new database does not need a backup");
    SqliteConnection.ClearAllPools();
    // 최신 DB
    await using (var current = NewExpenses(Path.Combine(needs, "current.db")))
    {
        await DatabaseMigrator.MigrateExpensesAsync(current);
        Check(!await DatabaseMigrator.NeedsMigrationAsync(current), "An up-to-date database does not need a backup");
    }
    // 마이그레이션 일부만 적용된 DB
    var partialPath = Path.Combine(needs, "partial.db");
    await using (var partialDb = NewExpenses(partialPath))
    {
        var migrations = partialDb.Database.GetMigrations().ToList();
        await partialDb.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(migrations[0]);
        Check(await DatabaseMigrator.NeedsMigrationAsync(partialDb), "A database with pending migrations needs a backup");
        await DatabaseMigrator.MigrateExpensesAsync(partialDb);
        Check(!await DatabaseMigrator.NeedsMigrationAsync(partialDb), "After migrating nothing is pending");
    }
    // 마이그레이션 도입 이전 DB: 업그레이드 전에 백업하면 옛 스키마 그대로 남음
    var legacyPath = Path.Combine(needs, "legacy.db");
    await using (var seed = NewExpenses(legacyPath))
    {
        await seed.Database.EnsureCreatedAsync();
        await seed.Database.ExecuteSqlRawAsync("DROP TABLE ExpenseTags; DROP TABLE Tags;");
        await ExpensesSchema.EnsureCreatedAsync(seed);
        seed.Expenses.Add(new ExpenseRecord { OwnerId = "A", Date = new DateTime(2026, 9, 1), Amount = 12_000, Category = "식비", Memo = "업그레이드 전" });
        await seed.SaveChangesAsync();
    }
    SqliteConnection.ClearAllPools();
    var preDir = Path.Combine(root, "pre-backups");
    var preService = NewService(preDir, 14, 3, legacyPath);
    await using (var legacyDb = NewExpenses(legacyPath))
    {
        Check(await DatabaseMigrator.NeedsMigrationAsync(legacyDb), "A pre-migration database needs a backup");
        var pre = await preService.CreateAsync(beforeMigration: true);
        Check(pre.BeforeMigration && pre.Name.EndsWith("-premigrate"), "Pre-migration name wrong");
        await DatabaseMigrator.MigrateExpensesAsync(legacyDb);
        Check(!await DatabaseMigrator.NeedsMigrationAsync(legacyDb) && Convert.ToInt64(ScalarDb(legacyDb, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Tags'")) == 1, "Live database must be upgraded");
        using (var snapshot = Open(Path.Combine(preDir, pre.Name, "legacy.db")))
            Check(Convert.ToInt64(Scalar(snapshot, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Tags'")) == 0 && Convert.ToInt64(Scalar(snapshot, "SELECT COUNT(*) FROM Expenses")) == 1, "The pre-migration backup must keep the old schema and data");
        // 복원: 백업 파일을 그대로 되돌려 쓰면 앱이 다시 같은 방식으로 업그레이드하고 데이터는 그대로
        SqliteConnection.ClearAllPools();
        var restoredPath = Path.Combine(needs, "restored.db");
        File.Copy(Path.Combine(preDir, pre.Name, "legacy.db"), restoredPath, true);
        await using var restored = NewExpenses(restoredPath);
        Check(await DatabaseMigrator.NeedsMigrationAsync(restored), "A restored old backup must be upgraded again");
        await DatabaseMigrator.MigrateExpensesAsync(restored);
        Check((await restored.Expenses.SingleAsync()).Memo == "업그레이드 전" && Convert.ToInt64(ScalarDb(restored, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Tags'")) == 1, "Restored backup lost data after the upgrade");
    }
}

static ExpensesDbContext NewExpenses(string path) => new(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);

static AuthDbContext NewAuth(string path)
{
    var services = new ServiceCollection();
    services.AddIdentityCore<IdentityUser>();
    return new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite($"Data Source={path};Pooling=False")
        .UseApplicationServiceProvider(services.BuildServiceProvider()).Options);
}

static SqliteConnection Open(string file)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    connection.Open();
    return connection;
}

static object? Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
}

static object? ScalarDb(DbContext db, string sql)
{
    db.Database.OpenConnection();
    using var command = db.Database.GetDbConnection().CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
}
