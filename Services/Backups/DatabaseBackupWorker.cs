using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyExpenses.Services.Backups;

// 앱이 켜져 있는 동안 정해진 주기마다 데이터베이스를 백업하고 오래된 백업을 정리합니다.
public sealed class DatabaseBackupWorker(DatabaseBackupService backups, ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 앱이 막 시작할 때의 작업이 끝난 뒤에 첫 백업을 합니다.
            await Task.Delay(backups.Options.StartDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
                await Task.Delay(await RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException) { /* 종료 */ }
    }

    // 백업할 때가 되었으면 만들고 정리한 뒤, 다음 확인까지 기다릴 시간을 돌려줍니다. 실패하면 한 시간 뒤에 다시 시도합니다.
    public async Task<TimeSpan> RunOnceAsync(DateTimeOffset current, CancellationToken cancellationToken)
    {
        var interval = backups.Options.Interval;
        try
        {
            var wait = DatabaseBackupService.NextDelay(backups.LastScheduledAt(), current, interval);
            if (wait > TimeSpan.Zero) return wait;
            await backups.CreateAsync(beforeMigration: false, cancellationToken);
            backups.Prune();
            return interval;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "정기 데이터베이스 백업에 실패했습니다. {Delay}분 뒤에 다시 시도합니다.", RetryDelay.TotalMinutes);
            return RetryDelay;
        }
    }
}
