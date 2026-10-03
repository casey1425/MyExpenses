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
        // 지출과 수입을 같은 시점에 읽어 월별 순수지가 서로 어긋나지 않게 합니다.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Date >= firstDate && item.Date <= lastDate)
            .ToListAsync(cancellationToken);
        var incomes = await db.Incomes.AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.Date >= firstDate && item.Date <= lastDate)
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await CategoryService.EnsureAsync(db, ownerId, cancellationToken);
        var categories = await db.UserCategories.Where(c => c.OwnerId == ownerId).OrderBy(c => c.Position).ThenBy(c => c.Id)
            .Select(c => c.Name).ToListAsync(cancellationToken);
        return ExpenseTrends.Calculate(expenses, month, today, categories, incomes);
    }
}
