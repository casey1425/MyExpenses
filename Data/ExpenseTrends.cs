namespace MyExpenses.Data;

public sealed record MonthlyExpenseTotal(DateOnly Month, decimal Amount, int Count, bool IsPartial);

public sealed record ExpenseMonthComparison(decimal CurrentAmount, decimal PreviousAmount,
    int CurrentCount, int PreviousCount)
{
    public decimal Difference => CurrentAmount - PreviousAmount;
    public decimal? PercentageChange => PreviousAmount > 0 ? Difference / PreviousAmount * 100 : null;
}

public sealed record CategoryMonthComparison(string Category, ExpenseMonthComparison Comparison);

public sealed record ExpenseTrendsReport(DateOnly Month, DateOnly CurrentEnd,
    DateOnly PreviousMonth, DateOnly PreviousEnd, bool IsCurrentMonth,
    IReadOnlyList<MonthlyExpenseTotal> Months, ExpenseMonthComparison Comparison,
    IReadOnlyList<CategoryMonthComparison> Categories);

public static class ExpenseTrends
{
    public static readonly DateOnly MinimumMonth = new(1, 6, 1);
    private static readonly string[] Categories = ["식비", "카페", "교통", "쇼핑", "생활", "기타"];

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static ExpenseTrendsReport Calculate(IEnumerable<ExpenseRecord> expenses, DateOnly month, DateOnly today)
    {
        month = MonthStart(month);
        if (month < MinimumMonth || month > MonthStart(today))
            throw new ArgumentOutOfRangeException(nameof(month));

        var isCurrentMonth = month == MonthStart(today);
        var currentEnd = isCurrentMonth ? today : MonthEnd(month);
        var previousMonth = month.AddMonths(-1);
        var previousEnd = isCurrentMonth
            ? new DateOnly(previousMonth.Year, previousMonth.Month,
                Math.Min(today.Day, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month)))
            : MonthEnd(previousMonth);
        var firstMonth = month.AddMonths(-5);
        var records = expenses.Where(item => DateOnly.FromDateTime(item.Date) >= firstMonth &&
                                            DateOnly.FromDateTime(item.Date) <= currentEnd).ToList();
        var current = records.Where(item => DateOnly.FromDateTime(item.Date) >= month).ToList();
        var previous = records.Where(item => DateOnly.FromDateTime(item.Date) >= previousMonth &&
                                             DateOnly.FromDateTime(item.Date) <= previousEnd).ToList();
        var groupedMonths = records.GroupBy(item => MonthStart(DateOnly.FromDateTime(item.Date)))
            .ToDictionary(group => group.Key, group => (Amount: group.Sum(item => (decimal)item.Amount), Count: group.Count()));
        var months = Enumerable.Range(0, 6).Select(index =>
        {
            var target = firstMonth.AddMonths(index);
            var total = groupedMonths.GetValueOrDefault(target);
            return new MonthlyExpenseTotal(target, total.Amount, total.Count,
                isCurrentMonth && target == month);
        }).ToList();

        var categoryNames = Categories.Concat(current.Select(item => item.Category))
            .Concat(previous.Select(item => item.Category)).Distinct();
        var comparisons = categoryNames.Select(category => new CategoryMonthComparison(category,
            Compare(current.Where(item => item.Category == category), previous.Where(item => item.Category == category))))
            .ToList();

        return new ExpenseTrendsReport(month, currentEnd, previousMonth, previousEnd,
            isCurrentMonth, months, Compare(current, previous), comparisons);
    }

    private static DateOnly MonthEnd(DateOnly month) =>
        new(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

    private static ExpenseMonthComparison Compare(IEnumerable<ExpenseRecord> current, IEnumerable<ExpenseRecord> previous)
    {
        var currentRecords = current.ToList();
        var previousRecords = previous.ToList();
        return new ExpenseMonthComparison(currentRecords.Sum(item => (decimal)item.Amount),
            previousRecords.Sum(item => (decimal)item.Amount), currentRecords.Count, previousRecords.Count);
    }
}
