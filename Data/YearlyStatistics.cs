using System.Globalization;

namespace MyExpenses.Data;

public sealed record YearMonthRow(DateOnly Month, long Income, long Expense, int IncomeCount, int ExpenseCount,
    bool IsFuture, bool IsPartial)
{
    public long Net => Income - Expense;

    // 수입이 없으면 저축률을 계산할 수 없으므로 null입니다.
    public double? SavingsRate => Income > 0 ? Math.Round((double)Net * 100 / Income, 1) : null;
}

public sealed record YearTotals(long Income, long Expense, int IncomeCount, int ExpenseCount)
{
    public long Net => Income - Expense;
    public double? SavingsRate => Income > 0 ? Math.Round((double)Net * 100 / Income, 1) : null;
}

public sealed record YearCategoryRow(string Category, long Amount, int Count, double Percentage, long PreviousAmount)
{
    public long Difference => Amount - PreviousAmount;
    public double? PercentageChange => PreviousAmount > 0 ? Math.Round((double)Difference * 100 / PreviousAmount, 1) : null;
    public string WidthStyle => $"width: {Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%";
}

public sealed record YearMethodRow(string Name, string Type, long Amount, int Count, double Percentage)
{
    public string WidthStyle => $"width: {Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%";
}

public sealed record YearTagRow(int TagId, string Name, long Amount, int Count, double Percentage)
{
    public string WidthStyle => $"width: {Math.Min(Percentage, 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
}

public sealed record TopExpenseRow(DateOnly Date, long Amount, string Category, string Memo);

public sealed record FrequentMemoRow(string Memo, int Count, long Amount);

public sealed record WeekdayRow(DayOfWeek Day, long Amount, int Count, double Percentage)
{
    public string WidthStyle => $"width: {Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%";
}

public sealed record YearlyReport(int Year, DateOnly From, DateOnly To, DateOnly PreviousFrom, DateOnly PreviousTo,
    bool IsCurrentYear, IReadOnlyList<YearMonthRow> Months, YearTotals Current, YearTotals Previous,
    long? AverageMonthlyExpense, int AverageMonthCount, IReadOnlyList<YearCategoryRow> Categories,
    IReadOnlyList<YearMethodRow> Methods, IReadOnlyList<TopExpenseRow> TopExpenses,
    IReadOnlyList<FrequentMemoRow> FrequentMemos, IReadOnlyList<WeekdayRow> Weekdays,
    IReadOnlyList<YearTagRow>? Tags = null, long UntaggedAmount = 0, int UntaggedCount = 0);

public static class YearlyStatistics
{
    public const int MinimumYear = 2;
    public const int TopCount = 10;
    public const int MinimumMemoRepeats = 2;

    // 이번 해는 오늘까지, 지난 해는 한 해 전체를 집계합니다. 비교 기간은 같은 날짜까지입니다.
    public static YearlyReport Calculate(IEnumerable<ExpenseRecord> expenses, IEnumerable<IncomeRecord> incomes,
        IReadOnlyDictionary<int, PaymentMethod> methods, int year, DateOnly today)
    {
        if (year < MinimumYear || year > today.Year) throw new ArgumentOutOfRangeException(nameof(year));

        var isCurrent = year == today.Year;
        var from = new DateOnly(year, 1, 1);
        var to = isCurrent ? today : new DateOnly(year, 12, 31);
        var previousFrom = new DateOnly(year - 1, 1, 1);
        var previousTo = isCurrent
            ? new DateOnly(year - 1, today.Month, Math.Min(today.Day, DateTime.DaysInMonth(year - 1, today.Month)))
            : new DateOnly(year - 1, 12, 31);

        var current = expenses.Where(e => InRange(e.Date, from, to)).OrderBy(e => e.Id).ToList();
        var previous = expenses.Where(e => InRange(e.Date, previousFrom, previousTo)).ToList();
        var currentIncomes = incomes.Where(i => InRange(i.Date, from, to)).ToList();
        var previousIncomes = incomes.Where(i => InRange(i.Date, previousFrom, previousTo)).ToList();

        var expenseByMonth = current.GroupBy(e => e.Date.Month)
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(e => e.Amount), Count: g.Count()));
        var incomeByMonth = currentIncomes.GroupBy(i => i.Date.Month)
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(i => i.Amount), Count: g.Count()));
        var months = Enumerable.Range(1, 12).Select(month =>
        {
            var expense = expenseByMonth.GetValueOrDefault(month);
            var income = incomeByMonth.GetValueOrDefault(month);
            return new YearMonthRow(new DateOnly(year, month, 1), income.Amount, expense.Amount, income.Count, expense.Count,
                IsFuture: isCurrent && month > today.Month, IsPartial: isCurrent && month == today.Month);
        }).ToList();

        var currentTotals = Totals(current, currentIncomes);
        var previousTotals = Totals(previous, previousIncomes);

        // 진행 중인 달은 일부만 지났으므로 월 평균에서 빼서 평균이 낮게 나오지 않게 합니다.
        var averageMonths = months.Where(m => !m.IsFuture && !m.IsPartial).ToList();
        long? average = averageMonths.Count > 0 ? averageMonths.Sum(m => m.Expense) / averageMonths.Count : null;

        var previousByCategory = previous.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var categories = current.GroupBy(e => e.Category)
            .Select(g => (Category: g.Key, Amount: g.Sum(e => e.Amount), Count: g.Count()))
            .ToList();
        foreach (var category in previousByCategory.Keys.Where(c => categories.All(row => row.Category != c)))
            categories.Add((category, 0, 0));
        var categoryRows = categories
            .Select(row => new YearCategoryRow(row.Category, row.Amount, row.Count,
                Share(row.Amount, currentTotals.Expense), previousByCategory.GetValueOrDefault(row.Category)))
            .OrderByDescending(row => row.Amount).ThenByDescending(row => row.PreviousAmount)
            .ThenBy(row => row.Category, StringComparer.Ordinal).ToList();

        var methodRows = current.GroupBy(e => e.PaymentMethodId is int id && methods.ContainsKey(id) ? id : (int?)null)
            .Select(g =>
            {
                var method = g.Key is int id ? methods[id] : null;
                var amount = g.Sum(e => e.Amount);
                return new YearMethodRow(method?.Name ?? "미지정", method?.Type ?? "", amount, g.Count(),
                    Share(amount, currentTotals.Expense));
            })
            .OrderByDescending(row => row.Amount).ThenBy(row => row.Name, StringComparer.Ordinal).ToList();

        var top = current.OrderByDescending(e => e.Amount).ThenByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Take(TopCount).Select(e => new TopExpenseRow(DateOnly.FromDateTime(e.Date), e.Amount, e.Category, e.Memo.Trim()))
            .ToList();

        // 같은 메모는 대소문자와 앞뒤 공백을 무시하고 묶습니다.
        var memos = current.Where(e => !string.IsNullOrWhiteSpace(e.Memo))
            .GroupBy(e => e.Memo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinimumMemoRepeats)
            .Select(g => new FrequentMemoRow(g.Key, g.Count(), g.Sum(e => e.Amount)))
            .OrderByDescending(row => row.Count).ThenByDescending(row => row.Amount)
            .ThenBy(row => row.Memo, StringComparer.Ordinal).Take(TopCount).ToList();

        var weekdayGroups = current.GroupBy(e => e.Date.DayOfWeek)
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(e => e.Amount), Count: g.Count()));
        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }
            .Select(day =>
            {
                var group = weekdayGroups.GetValueOrDefault(day);
                return new WeekdayRow(day, group.Amount, group.Count, Share(group.Amount, currentTotals.Expense));
            }).ToList();

        // 한 지출에 태그가 여러 개면 각 태그에 모두 들어가므로 태그별 합계를 더하면 전체 지출보다 클 수 있습니다.
        var tagRows = current.SelectMany(e => e.TagLinks.Where(link => link.Tag is not null).Select(link => (Expense: e, link.TagId, Name: link.Tag!.Name)))
            .GroupBy(item => item.TagId)
            .Select(g => new YearTagRow(g.Key, g.First().Name, g.Sum(item => item.Expense.Amount), g.Count(), Share(g.Sum(item => item.Expense.Amount), currentTotals.Expense)))
            .OrderByDescending(row => row.Amount).ThenBy(row => row.Name, StringComparer.Ordinal).Take(TopCount * 2).ToList();
        var untagged = current.Where(e => !e.TagLinks.Any(link => link.Tag is not null)).ToList();

        return new YearlyReport(year, from, to, previousFrom, previousTo, isCurrent, months, currentTotals, previousTotals,
            average, averageMonths.Count, categoryRows, methodRows, top, memos, weekdays, tagRows, untagged.Sum(e => e.Amount), untagged.Count);
    }

    public static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "월",
        DayOfWeek.Tuesday => "화",
        DayOfWeek.Wednesday => "수",
        DayOfWeek.Thursday => "목",
        DayOfWeek.Friday => "금",
        DayOfWeek.Saturday => "토",
        _ => "일"
    };

    private static bool InRange(DateTime date, DateOnly from, DateOnly to)
    {
        var value = DateOnly.FromDateTime(date);
        return value >= from && value <= to;
    }

    private static YearTotals Totals(IReadOnlyCollection<ExpenseRecord> expenses, IReadOnlyCollection<IncomeRecord> incomes) =>
        new(incomes.Sum(i => i.Amount), expenses.Sum(e => e.Amount), incomes.Count, expenses.Count);

    private static double Share(long amount, long total) => total > 0 ? (double)amount * 100 / total : 0;
}
