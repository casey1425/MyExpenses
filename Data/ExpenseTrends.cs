namespace MyExpenses.Data;

public sealed record MonthlyExpenseTotal(DateOnly Month, decimal Amount, int Count, bool IsPartial);

public sealed record ExpenseMonthComparison(decimal CurrentAmount, decimal PreviousAmount,
    int CurrentCount, int PreviousCount)
{
    public decimal Difference => CurrentAmount - PreviousAmount;
    public decimal? PercentageChange => PreviousAmount > 0 ? Difference / PreviousAmount * 100 : null;
}

public sealed record MonthlyCashflowPoint(DateOnly Month, decimal Income, decimal Expense, int IncomeCount, bool IsPartial)
{
    public decimal Net => Income - Expense;

    // 수입이 없으면 저축률을 계산할 수 없으므로 null입니다.
    public double? SavingsRate => Income > 0 ? Math.Round((double)(Net * 100 / Income), 1) : null;
}

public sealed record CategoryMonthComparison(string Category, ExpenseMonthComparison Comparison);

public sealed record ExpenseTrendsReport(DateOnly Month, DateOnly CurrentEnd,
    DateOnly PreviousMonth, DateOnly PreviousEnd, bool IsCurrentMonth,
    IReadOnlyList<MonthlyExpenseTotal> Months, ExpenseMonthComparison Comparison,
    IReadOnlyList<CategoryMonthComparison> Categories,
    IReadOnlyList<MonthlyCashflowPoint>? CashflowMonths = null);

public static class ExpenseTrends
{
    public static readonly DateOnly MinimumMonth = new(1, 6, 1);

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static ExpenseTrendsReport Calculate(IEnumerable<ExpenseRecord> expenses, DateOnly month, DateOnly today, IReadOnlyList<string>? categories = null,
        IEnumerable<IncomeRecord>? incomes = null)
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

        var categoryNames = (categories ?? ExpenseCategories.All).Concat(current.Select(item => item.Category))
            .Concat(previous.Select(item => item.Category)).Distinct();
        var comparisons = categoryNames.Select(category => new CategoryMonthComparison(category,
            Compare(current.Where(item => item.Category == category), previous.Where(item => item.Category == category))))
            .ToList();

        // 수입 목록이 주어졌을 때만 같은 6개월 구간의 수입·순수지를 계산합니다.
        IReadOnlyList<MonthlyCashflowPoint>? cashflow = null;
        if (incomes is not null)
        {
            var incomeByMonth = incomes
                .Where(item => DateOnly.FromDateTime(item.Date) >= firstMonth &&
                               DateOnly.FromDateTime(item.Date) <= currentEnd)
                .GroupBy(item => MonthStart(DateOnly.FromDateTime(item.Date)))
                .ToDictionary(group => group.Key, group => (Amount: group.Sum(item => (decimal)item.Amount), Count: group.Count()));
            cashflow = months.Select(point =>
            {
                var income = incomeByMonth.GetValueOrDefault(point.Month);
                return new MonthlyCashflowPoint(point.Month, income.Amount, point.Amount, income.Count, point.IsPartial);
            }).ToList();
        }

        return new ExpenseTrendsReport(month, currentEnd, previousMonth, previousEnd,
            isCurrentMonth, months, Compare(current, previous), comparisons, cashflow);
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
