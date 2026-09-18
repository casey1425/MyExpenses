using System.Globalization;

namespace MyExpenses.Data;

public sealed record CategoryStatistic(string Category, long Amount, int Count, double Percentage)
{
    public string WidthStyle => $"width: {Percentage.ToString("0.##", CultureInfo.InvariantCulture)}%";
}

public static class ExpenseStatistics
{
    public static IReadOnlyList<CategoryStatistic> ByCategory(IEnumerable<ExpenseRecord> expenses)
    {
        var groups = expenses
            .GroupBy(expense => expense.Category)
            .Select(group => new
            {
                Category = group.Key,
                Amount = group.Sum(expense => expense.Amount),
                Count = group.Count()
            })
            .ToArray();

        var total = groups.Sum(group => group.Amount);

        return groups
            .Select(group => new CategoryStatistic(
                group.Category,
                group.Amount,
                group.Count,
                total > 0 ? (double)group.Amount / total * 100 : 0))
            .OrderByDescending(statistic => statistic.Amount)
            .ThenBy(statistic => statistic.Category)
            .ToArray();
    }
}
