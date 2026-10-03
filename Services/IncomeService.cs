using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record IncomeInput(DateTime Date, long Amount, string Source, string Memo);

public sealed class IncomeService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<IncomeRecord>> ListAsync(string ownerId, DateTime month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var (start, end) = MonthRange(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Incomes.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Date >= start && i.Date < end)
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IncomeRecord> AddAsync(string ownerId, IncomeInput input, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, input);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var income = new IncomeRecord { OwnerId = ownerId };
        Apply(income, input);
        db.Incomes.Add(income);
        await db.SaveChangesAsync(cancellationToken);
        return income;
    }

    public async Task<IncomeRecord?> UpdateAsync(string ownerId, int id, IncomeInput input, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, input);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var income = await db.Incomes.SingleOrDefaultAsync(i => i.OwnerId == ownerId && i.Id == id, cancellationToken);
        if (income is null) return null;
        Apply(income, input);
        await db.SaveChangesAsync(cancellationToken);
        return income;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Incomes.Where(i => i.OwnerId == ownerId && i.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    // 수입과 지출을 한 트랜잭션에서 읽어 같은 시점의 월별 현금흐름을 만듭니다.
    public async Task<MonthlyCashflow> CashflowAsync(string ownerId, DateTime month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var (start, end) = MonthRange(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var incomes = await db.Incomes.AsNoTracking()
            .Where(i => i.OwnerId == ownerId && i.Date >= start && i.Date < end)
            .Select(i => i.Amount).ToListAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.Date >= start && e.Date < end)
            .Select(e => e.Amount).ToListAsync(cancellationToken);
        return new MonthlyCashflow(incomes.Sum(), expenses.Sum());
    }

    private static (DateTime Start, DateTime End) MonthRange(DateTime month)
    {
        var start = new DateTime(month.Year, month.Month, 1);
        return (start, start.AddMonths(1));
    }

    private static void Validate(string ownerId, IncomeInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (input.Date == default || input.Amount <= 0) throw new ArgumentException("날짜와 1원 이상의 금액을 입력해 주세요.");
        if (!IncomeRecord.Sources.Contains(input.Source) || input.Memo is null || input.Memo.Trim().Length > 100)
            throw new ArgumentException("수입 분류를 선택하고 100자 이하 메모를 입력해 주세요.");
    }

    private static void Apply(IncomeRecord income, IncomeInput input)
    {
        income.Date = input.Date.Date;
        income.Amount = input.Amount;
        income.Source = input.Source;
        income.Memo = input.Memo.Trim();
    }
}
