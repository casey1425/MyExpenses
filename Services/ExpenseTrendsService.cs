using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class ExpenseTrendsService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<ExpenseTrendsReport> LoadAsync(string ownerId, DateOnly month, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        month = ExpenseTrends.MonthStart(month);
        if (month < ExpenseTrends.MinimumMonth || month > ExpenseTrends.MonthStart(today))
            throw new ArgumentOutOfRangeException(nameof(month));

        var firstDate = month.AddMonths(-5).ToDateTime(TimeOnly.MinValue);
        var lastDate = (month == ExpenseTrends.MonthStart(today) ? today :
            new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)))
            .ToDateTime(TimeOnly.MaxValue);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Date >= firstDate && item.Date <= lastDate)
            .ToListAsync(cancellationToken);
        return ExpenseTrends.Calculate(expenses, month, today);
    }
}
