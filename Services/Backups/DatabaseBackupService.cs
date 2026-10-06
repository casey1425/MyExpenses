using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyExpenses.Services.Backups;

public sealed record DatabaseBackupInfo(string Name, DateTimeOffset CreatedAt, bool BeforeMigration, long Bytes, IReadOnlyList<string> Files);

// SQLite 파일을 서버에서 통째로 복사해 둡니다. 실행 중에도 일관된 스냅숏을 만들기 위해 SQLite의 온라인 백업 API를 쓰고,
// 복사본을 점검한 뒤에만 완성된 백업으로 인정합니다. 사용자별 백업 파일(/backup)과 달리 모든 사용자의 데이터가 들어 있으므로
// 화면으로 내려받는 기능은 없고, 서버 관리자가 파일로 복원합니다.
public sealed partial class DatabaseBackupService(DatabaseBackupOptions options, IReadOnlyList<string> databaseFiles,
    ILogger<DatabaseBackupService> logger, Func<DateTimeOffset>? clock = null,
    // 아래 세 값은 검증에서 잠금 상황을 재현하기 위한 것입니다. 보통은 기본값을 씁니다.
    int lockedAttempts = DatabaseBackupService.DefaultLockedAttempts, TimeSpan? lockedRetryDelay = null,
    Action<string, string>? copyOnce = null)
{
    public const int DefaultLockedAttempts = 40;
    private readonly TimeSpan retryDelay = lockedRetryDelay ?? TimeSpan.FromMilliseconds(250);
    private readonly Action<string, string> copy = copyOnce ?? CopyOnce;
    private const string BeforeMigrationSuffix = "-premigrate";
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);

    public DatabaseBackupOptions Options => options;

    [GeneratedRegex(@"^(?<date>\d{8})-(?<time>\d{6})(?<n>-\d+)?(?<pre>-premigrate)?$")]
    private static partial Regex NamePattern();

    // 백업을 하나 만듭니다. 실패하면 만들던 폴더를 지우고 예외를 던져, 반쯤 만들어진 백업이 남지 않습니다.
    public async Task<DatabaseBackupInfo> CreateAsync(bool beforeMigration = false, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var sources = databaseFiles.Where(File.Exists).ToList();
            if (sources.Count == 0) throw new InvalidOperationException("백업할 데이터베이스 파일이 없습니다.");

            EnsureDirectory(options.Directory);
            var stamp = now().ToOffset(KoreaOffset);
            string name;
            do
            {
                name = stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (beforeMigration ? BeforeMigrationSuffix : "");
                stamp = stamp.AddSeconds(1);
            } while (Directory.Exists(Path.Combine(options.Directory, name)));

            var temp = Path.Combine(options.Directory, ".tmp-" + name);
            var final = Path.Combine(options.Directory, name);
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            EnsureDirectory(temp);
            try
            {
                foreach (var source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = Path.Combine(temp, Path.GetFileName(source));
                    await Task.Run(() => CopyAndVerify(source, target, copy, lockedAttempts, retryDelay), cancellationToken);
                }
                Directory.Move(temp, final);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }

            var info = Describe(new DirectoryInfo(final))!;
            logger.LogInformation("데이터베이스 백업을 만들었습니다: {Name} ({Files}개 파일, {Bytes:N0} bytes{Reason})", info.Name,
                info.Files.Count, info.Bytes, beforeMigration ? ", 마이그레이션 전" : "");
            return info;
        }
        finally { gate.Release(); }
    }

    // 최신순으로 돌려줍니다. 우리가 만든 이름 형식이 아닌 폴더·파일은 건드리지 않고 무시합니다.
    public IReadOnlyList<DatabaseBackupInfo> List()
    {
        if (!Directory.Exists(options.Directory)) return [];
        return new DirectoryInfo(options.Directory).EnumerateDirectories()
            .Where(directory => directory.LinkTarget is null)
            .Select(Describe).OfType<DatabaseBackupInfo>()
            .OrderByDescending(info => info.CreatedAt).ThenByDescending(info => info.Name, StringComparer.Ordinal).ToList();
    }

    // 정기 백업은 최근 Keep개, 마이그레이션 전 백업은 최근 KeepBeforeMigration개만 남깁니다. 지운 개수를 돌려줍니다.
    public int Prune()
    {
        var deleted = 0;
        var all = List();
        foreach (var stale in all.Where(info => !info.BeforeMigration).Skip(options.Keep)
                     .Concat(all.Where(info => info.BeforeMigration).Skip(options.KeepBeforeMigration)))
        {
            if (TryDelete(Path.Combine(options.Directory, stale.Name))) deleted++;
        }

        // 비정상 종료로 남은 임시 폴더는 한 시간이 지나면 정리합니다.
        if (Directory.Exists(options.Directory))
            foreach (var temp in new DirectoryInfo(options.Directory).EnumerateDirectories(".tmp-*").Where(d => now() - new DateTimeOffset(d.LastWriteTimeUtc, TimeSpan.Zero) > StaleTempAge))
                TryDelete(temp.FullName);
        if (deleted > 0) logger.LogInformation("오래된 데이터베이스 백업 {Count}개를 지웠습니다.", deleted);
        return deleted;
    }

    // 정기 백업 시각 기준으로 다음 백업까지 기다릴 시간입니다. 기록이 없거나 이미 늦었으면 바로, 시계가 거꾸로 간 경우는 한 주기 뒤입니다.
    public static TimeSpan NextDelay(DateTimeOffset? lastScheduled, DateTimeOffset current, TimeSpan interval)
    {
        if (lastScheduled is not DateTimeOffset last) return TimeSpan.Zero;
        var elapsed = current - last;
        if (elapsed < TimeSpan.Zero) return interval;
        return elapsed >= interval ? TimeSpan.Zero : interval - elapsed;
    }

    public DateTimeOffset? LastScheduledAt() => List().Where(info => !info.BeforeMigration).Select(info => (DateTimeOffset?)info.CreatedAt).FirstOrDefault();

    // 앱이 쓰는 도중이면 SQLite가 "database is locked"(BUSY/LOCKED)를 돌려줄 수 있어, 잠금이 풀릴 때까지 잠깐씩 기다리며 다시 시도합니다.
    private static void CopyAndVerify(string source, string target, Action<string, string> copy, int maxAttempts, TimeSpan delay)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                copy(source, target);
                return;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6 && attempt < maxAttempts)
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(target)) File.Delete(target);
                Thread.Sleep(delay);
            }
        }
    }

    private static void CopyOnce(string source, string target)
    {
        var sourceString = new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 30 }.ToString();
        var targetString = new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
        using (var from = new SqliteConnection(sourceString))
        using (var to = new SqliteConnection(targetString))
        {
            from.Open();
            to.Open();
            from.BackupDatabase(to);
            using var check = to.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (result != "ok") throw new InvalidOperationException($"백업본 점검에 실패했습니다({Path.GetFileName(source)}): {result}");
        }
        SqliteConnection.ClearAllPools();
    }

    private static void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        // 모든 사용자의 데이터가 들어 있으므로 Linux에서는 소유자만 접근하게 합니다.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private bool TryDelete(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (!info.Exists || info.LinkTarget is not null) return false;
            info.Delete(recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "백업 폴더를 지우지 못했습니다: {Path}", path);
            return false;
        }
    }

    private static DatabaseBackupInfo? Describe(DirectoryInfo directory)
    {
        var match = NamePattern().Match(directory.Name);
        if (!match.Success) return null;
        if (!DateTimeOffset.TryParseExact(match.Groups["date"].Value + match.Groups["time"].Value + "+09:00", "yyyyMMddHHmmsszzz",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)) return null;
        var files = directory.EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
        return new DatabaseBackupInfo(directory.Name, created, match.Groups["pre"].Success, files.Sum(file => file.Length), files.Select(file => file.Name).ToList());
    }
}
