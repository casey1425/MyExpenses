using Microsoft.Extensions.Configuration;

namespace MyExpenses.Services.Backups;

// 서버 자동 백업 설정입니다. 잘못된 값은 기본값으로 바꾸고 이유를 Warnings에 남깁니다.
public sealed record DatabaseBackupOptions(bool Enabled, TimeSpan Interval, int Keep, int KeepBeforeMigration,
    string Directory, TimeSpan StartDelay, IReadOnlyList<string> Warnings)
{
    public const int DefaultIntervalHours = 24;
    public const int DefaultKeep = 14;
    public const int DefaultKeepBeforeMigration = 3;
    public const int DefaultStartDelaySeconds = 30;

    public static DatabaseBackupOptions From(IConfiguration configuration, string dataDirectory)
    {
        var warnings = new List<string>();
        int Number(string key, int fallback, int min, int max)
        {
            var raw = configuration[$"Backup:{key}"];
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
                return value;
            warnings.Add($"Backup:{key} 값 ‘{raw}’은(는) {min}~{max} 사이의 정수여야 해서 기본값 {fallback}을(를) 사용합니다.");
            return fallback;
        }

        var enabledRaw = configuration["Backup:Enabled"];
        var enabled = true;
        if (!string.IsNullOrWhiteSpace(enabledRaw) && !bool.TryParse(enabledRaw, out enabled))
        {
            enabled = true;
            warnings.Add($"Backup:Enabled 값 ‘{enabledRaw}’은(는) true 또는 false여야 해서 기본값 true를 사용합니다.");
        }

        var configuredDirectory = configuration["Backup:Directory"];
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(dataDirectory, "backups")
            : Path.GetFullPath(configuredDirectory, dataDirectory);

        return new DatabaseBackupOptions(enabled, TimeSpan.FromHours(Number("IntervalHours", DefaultIntervalHours, 1, 24 * 30)),
            Number("Keep", DefaultKeep, 1, 365), Number("KeepBeforeMigration", DefaultKeepBeforeMigration, 0, 20), directory,
            TimeSpan.FromSeconds(Number("StartDelaySeconds", DefaultStartDelaySeconds, 0, 3600)), warnings);
    }
}
