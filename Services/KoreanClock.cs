namespace MyExpenses.Services;

// 서버 시간대와 무관하게 사용자에게 보이는 "오늘"을 한국 시간으로 계산합니다.
public static class KoreanClock
{
    public static DateTime Today =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Seoul").Date;
}
