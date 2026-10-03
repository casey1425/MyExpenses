namespace MyExpenses.Data;

public sealed class SavingsGoal
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // 지출·수입과 같이 정수 원 단위로 저장합니다.
    public long TargetAmount { get; set; }

    // 목표일은 선택 사항입니다.
    public DateTime? TargetDate { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class SavingsDeposit
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public int GoalId { get; set; }
    public DateTime Date { get; set; }
    public long Amount { get; set; }
    public string Memo { get; set; } = string.Empty;
}

public sealed record SavingsGoalProgress(
    long Saved,
    long Remaining,
    double Percent,
    bool IsAchieved,
    bool IsOverdue,
    int? DaysLeft,
    int? RemainingMonths,
    long? RequiredPerMonth,
    long AverageMonthlyDeposit,
    int? MonthsToGo,
    DateOnly? ProjectedMonth,
    bool ProjectedAfterTarget);

public static class SavingsGoalCalculator
{
    // 예상 달성 시점이 이 개월 수를 넘으면 의미가 없으므로 계산하지 않습니다.
    public const int MaxProjectionMonths = 1200;

    public static SavingsGoalProgress Calculate(SavingsGoal goal, IReadOnlyCollection<SavingsDeposit> deposits, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(deposits);
        today = today.Date;

        var saved = deposits.Sum(deposit => deposit.Amount);
        var remaining = Math.Max(0, goal.TargetAmount - saved);
        var achieved = goal.TargetAmount > 0 && saved >= goal.TargetAmount;
        // 반올림하면 달성 전인데 100%로 보일 수 있어 소수 첫째 자리에서 내림합니다.
        var percent = goal.TargetAmount > 0
            ? Math.Min(100d, Math.Floor(saved * 1000d / goal.TargetAmount) / 10d)
            : 0d;

        var target = goal.TargetDate?.Date;
        var overdue = !achieved && target is DateTime due && due < today;
        int? daysLeft = null, remainingMonths = null;
        long? requiredPerMonth = null;
        if (!achieved && target is DateTime targetDate)
        {
            daysLeft = (targetDate - today).Days;
            if (overdue)
            {
                remainingMonths = 0;
                requiredPerMonth = remaining; // 기한이 지나 남은 금액 전체가 필요합니다.
            }
            else
            {
                // 이번 달을 포함해 목표일이 속한 달까지 저축할 수 있는 달 수입니다.
                var months = (targetDate.Year - today.Year) * 12 + targetDate.Month - today.Month + 1;
                remainingMonths = months;
                requiredPerMonth = (remaining + months - 1) / months;
            }
        }

        // 첫 저축이 있는 달부터 이번 달까지의 월평균 저축액으로 달성 시점을 예상합니다.
        long averageDeposit = 0;
        int? monthsToGo = null;
        DateOnly? projectedMonth = null;
        var afterTarget = false;
        if (deposits.Count > 0)
        {
            var first = deposits.Min(deposit => deposit.Date);
            var span = Math.Max(1, (today.Year - first.Year) * 12 + today.Month - first.Month + 1);
            var average = (decimal)saved / span;
            averageDeposit = (long)Math.Round(average, MidpointRounding.AwayFromZero);
            if (!achieved && average > 0)
            {
                var needed = Math.Ceiling(remaining / average);
                if (needed <= MaxProjectionMonths)
                {
                    monthsToGo = (int)needed;
                    projectedMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(monthsToGo.Value);
                    afterTarget = target is DateTime due2 && projectedMonth > new DateOnly(due2.Year, due2.Month, 1);
                }
            }
        }

        return new SavingsGoalProgress(saved, remaining, percent, achieved, overdue, daysLeft, remainingMonths,
            requiredPerMonth, averageDeposit, monthsToGo, projectedMonth, afterTarget);
    }
}
