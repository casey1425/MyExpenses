using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record CalendarData(CalendarMonthView View, IReadOnlyList<ExpenseRecord> Expenses, IReadOnlyList<IncomeRecord> Incomes);

public sealed class CalendarService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<CalendarData> LoadAsync(string ownerId, DateOnly month, DateOnly today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        month = new DateOnly(month.Year, month.Month, 1);
        if (month < CalendarMonth.MinimumMonth || month > CalendarMonth.MaximumMonth) throw new ArgumentOutOfRangeException(nameof(month));

        var from = month.ToDateTime(TimeOnly.MinValue);
        var to = month.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // 지출과 수입을 같은 시점에 읽어 날짜별 합계가 서로 어긋나지 않게 합니다.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.PaymentMethod)
            .Where(e => e.OwnerId == ownerId && e.Date >= from && e.Date < to)
            .OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var incomes = await db.Incomes.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Date >= from && i.Date < to)
            .OrderBy(i => i.Date).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CalendarData(CalendarMonth.Calculate(expenses, incomes, month, today), expenses, incomes);
    }
}
