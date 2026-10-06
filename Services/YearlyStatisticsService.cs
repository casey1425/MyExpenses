using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class YearlyStatisticsService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    // 기록이 있는 해부터 올해까지를 최신순으로 돌려줍니다. 기록이 없으면 올해만 돌려줍니다.
    public async Task<IReadOnlyList<int>> AvailableYearsAsync(string ownerId, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var firstExpense = await db.Expenses.AsNoTracking().Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Date).Select(e => (DateTime?)e.Date).FirstOrDefaultAsync(cancellationToken);
        var firstIncome = await db.Incomes.AsNoTracking().Where(i => i.OwnerId == ownerId)
            .OrderBy(i => i.Date).Select(i => (DateTime?)i.Date).FirstOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var first = new[] { firstExpense?.Year, firstIncome?.Year }.Where(y => y.HasValue).Select(y => y!.Value)
            .DefaultIfEmpty(today.Year).Min();
        first = Math.Clamp(first, YearlyStatistics.MinimumYear, today.Year);
        return Enumerable.Range(first, today.Year - first + 1).Reverse().ToList();
    }

    public async Task<YearlyReport> LoadAsync(string ownerId, int year, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (year < YearlyStatistics.MinimumYear || year > today.Year) throw new ArgumentOutOfRangeException(nameof(year));

        // 비교를 위해 전년도 1월 1일부터 읽습니다.
        var firstDate = new DateTime(year - 1, 1, 1);
        var lastDate = (year == today.Year ? today : new DateOnly(year, 12, 31)).ToDateTime(TimeOnly.MaxValue);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // 지출·수입·결제수단을 같은 시점에 읽어 서로 어긋나지 않게 합니다.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.TagLinks).ThenInclude(link => link.Tag).AsSplitQuery()
            .Where(e => e.OwnerId == ownerId && e.Date >= firstDate && e.Date <= lastDate).ToListAsync(cancellationToken);
        var incomes = await db.Incomes.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Date >= firstDate && i.Date <= lastDate).ToListAsync(cancellationToken);
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId)
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return YearlyStatistics.Calculate(expenses, incomes, methods, year, today);
    }
}
