using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record ExpenseInput(DateTime Date, long Amount, string Category, string Memo, int? PaymentMethodId = null);

public sealed class ExpenseService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<ExpenseRecord>> ListAsync(string ownerId, ExpenseFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Expenses.AsNoTracking().Include(e => e.PaymentMethod).Where(e => e.OwnerId == ownerId);
        return await filter.Order(filter.ApplyTo(query)).ToListAsync(cancellationToken);
    }

    public async Task ValidatePaymentAsync(string ownerId, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await PaymentMethodService.VerifyOwnedAsync(db, ownerId, paymentMethodId, cancellationToken);
    }

    public async Task<ExpenseRecord> AddAsync(string ownerId, ExpenseInput input, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, input);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PaymentMethodService.VerifyOwnedAsync(db, ownerId, input.PaymentMethodId, cancellationToken);
        var expense = new ExpenseRecord { OwnerId = ownerId };
        Apply(expense, input);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expense;
    }

    public async Task<ExpenseRecord?> UpdateAsync(string ownerId, int id, ExpenseInput input, CancellationToken cancellationToken = default)
    {
        Validate(ownerId, input);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PaymentMethodService.VerifyOwnedAsync(db, ownerId, input.PaymentMethodId, cancellationToken);
        var expense = await db.Expenses.SingleOrDefaultAsync(e => e.OwnerId == ownerId && e.Id == id, cancellationToken);
        if (expense is null) return null;
        Apply(expense, input);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return expense;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Expenses.Where(e => e.OwnerId == ownerId && e.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<int> DeleteAllAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Expenses.Where(e => e.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
    }

    private static void Validate(string ownerId, ExpenseInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (input.Date == default || input.Amount <= 0) throw new ArgumentException("날짜와 1원 이상의 금액을 입력해 주세요.");
        if (!ExpenseCategories.IsSupported(input.Category) || input.Memo is null || input.Memo.Trim().Length > 100)
            throw new ArgumentException("카테고리와 100자 이하 메모를 확인해 주세요.");
    }

    private static void Apply(ExpenseRecord expense, ExpenseInput input)
    {
        expense.Date = input.Date.Date;
        expense.Amount = input.Amount;
        expense.Category = input.Category;
        expense.Memo = input.Memo.Trim();
        expense.PaymentMethodId = input.PaymentMethodId;
    }
}
