using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class ExpenseTemplateService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<ExpenseTemplate>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExpenseTemplates.AsNoTracking().Include(t => t.PaymentMethod).Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(cancellationToken);
    }

    public async Task<ExpenseTemplate?> FindAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExpenseTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.OwnerId == ownerId && t.Id == id, cancellationToken);
    }

    // ID와 소유자는 입력 모델로 받지 않으며 모든 변경 쿼리에 소유자 조건을 적용합니다.
    public async Task<bool> SaveAsync(string ownerId, int? id, ExpenseTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var values = input.Validate();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await PaymentMethodService.VerifyOwnedAsync(db, ownerId, values.PaymentMethodId, cancellationToken);
        ExpenseTemplate? template;
        if (id is int existingId)
        {
            template = await db.ExpenseTemplates.SingleOrDefaultAsync(t => t.OwnerId == ownerId && t.Id == existingId, cancellationToken);
            if (template is null) return false;
        }
        else
        {
            template = new ExpenseTemplate { OwnerId = ownerId };
            db.ExpenseTemplates.Add(template);
        }
        template.Name = values.Name;
        template.Amount = values.Amount;
        template.Category = values.Category;
        template.Memo = values.Memo;
        template.PaymentMethodId = values.PaymentMethodId;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExpenseTemplates.Where(t => t.OwnerId == ownerId && t.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
