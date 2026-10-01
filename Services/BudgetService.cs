using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record BudgetSnapshot(long Spent, long? Amount,
    Dictionary<string, long> CategorySpent, Dictionary<string, long> CategoryAmounts);

public sealed class BudgetService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<BudgetSnapshot> LoadAsync(string ownerId, DateTime month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        month = MonthStart(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var records = await new ExpenseFilter(month).ApplyTo(db.Expenses.AsNoTracking().Where(e => e.OwnerId == ownerId))
            .Select(e => new { e.Category, e.Amount }).ToListAsync(cancellationToken);
        var amount = await db.MonthlyBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId && b.Month == month)
            .Select(b => (long?)b.Amount).SingleOrDefaultAsync(cancellationToken);
        var categories = await db.CategoryBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId && b.Month == month)
            .ToDictionaryAsync(b => b.Category, b => b.Amount, cancellationToken);
        return new(records.Sum(e => e.Amount), amount,
            records.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)), categories);
    }

    public async Task SaveAsync(string ownerId, DateTime month, long amount, string? category = null, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, category);
        if (amount <= 0) throw new ArgumentException("예산은 1원 이상의 정수로 입력해 주세요.");
        month = MonthStart(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (category is not null) await CategoryService.VerifyAsync(db, ownerId, category, true, cancellationToken);
        if (category is null)
        {
            var budget = await db.MonthlyBudgets.FindAsync([ownerId, month], cancellationToken);
            if (budget is null) db.MonthlyBudgets.Add(new MonthlyBudget { OwnerId = ownerId, Month = month, Amount = amount });
            else budget.Amount = amount;
        }
        else
        {
            var budget = await db.CategoryBudgets.FindAsync([ownerId, month, category], cancellationToken);
            if (budget is null) db.CategoryBudgets.Add(new CategoryBudget { OwnerId = ownerId, Month = month, Category = category, Amount = amount });
            else budget.Amount = amount;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(string ownerId, DateTime month, string? category = null, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, category);
        month = MonthStart(month);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (category is null)
            await db.MonthlyBudgets.Where(b => b.OwnerId == ownerId && b.Month == month).ExecuteDeleteAsync(cancellationToken);
        else
            await db.CategoryBudgets.Where(b => b.OwnerId == ownerId && b.Month == month && b.Category == category).ExecuteDeleteAsync(cancellationToken);
    }

    private static DateTime MonthStart(DateTime month) => new(month.Year, month.Month, 1);

    private static void Validate(string ownerId, string? category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (category is not null && (string.IsNullOrWhiteSpace(category) || category.Length > 30)) throw new ArgumentException("지원하지 않는 카테고리입니다.");
    }
}
