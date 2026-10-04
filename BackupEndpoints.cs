using System.Security.Claims;
using MyExpenses.Services;

namespace MyExpenses;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/export/backup.json", ExportAsync).RequireAuthorization();
        return endpoints;
    }

    public static async Task<IResult> ExportAsync(HttpContext context, BackupService backupService, CancellationToken cancellationToken)
    {
        var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var backup = await backupService.ExportAsync(ownerId, KoreanClock.Now, cancellationToken);
        // 백업 파일에는 개인 금융 정보가 모두 들어 있으므로 브라우저·프록시가 저장하지 않게 하고, 형식 추측을 막습니다.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(Data.BackupSerializer.Serialize(backup), "application/json; charset=utf-8",
            $"MyExpenses-backup-{KoreanClock.Today:yyyy-MM-dd}.json");
    }
}
