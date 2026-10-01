using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class PaymentMethodService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<PaymentMethod>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId)
            .OrderBy(m => m.Name).ThenBy(m => m.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> SaveAsync(string ownerId, int? id, string name, string type, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        name = name.Trim();
        if (name.Length is < 1 or > 50 || !PaymentMethod.Types.Contains(type))
            throw new ArgumentException("이름은 1~50자로 입력하고 결제 유형을 선택해 주세요.");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.PaymentMethods.AnyAsync(m => m.OwnerId == ownerId && m.Name == name && m.Id != id, cancellationToken))
            throw new ArgumentException("같은 이름의 결제수단이 있습니다. 다른 이름을 입력해 주세요.");
        var method = id is int existingId
            ? await db.PaymentMethods.SingleOrDefaultAsync(m => m.OwnerId == ownerId && m.Id == existingId, cancellationToken)
            : new PaymentMethod { OwnerId = ownerId };
        if (method is null) return false;
        if (id is null) db.PaymentMethods.Add(method);
        method.Name = name;
        method.Type = type;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.PaymentMethods.AnyAsync(m => m.OwnerId == ownerId && m.Id == id, cancellationToken)) return false;
        await db.Expenses.Where(e => e.OwnerId == ownerId && e.PaymentMethodId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PaymentMethodId, (int?)null), cancellationToken);
        await db.ExpenseTemplates.Where(t => t.OwnerId == ownerId && t.PaymentMethodId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PaymentMethodId, (int?)null), cancellationToken);
        await db.PaymentMethods.Where(m => m.OwnerId == ownerId && m.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // 저장 트랜잭션과 같은 컨텍스트에서 검사하여 타 계정·삭제된 수단을 연결하지 않습니다.
    public static async Task VerifyOwnedAsync(ExpensesDbContext db, string ownerId, int? id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (id.HasValue && !await db.PaymentMethods.AnyAsync(m => m.OwnerId == ownerId && m.Id == id, cancellationToken))
            throw new ArgumentException("결제수단이 삭제되었거나 사용할 수 없습니다. 목록을 새로고침해 주세요.");
    }

    public async Task<List<PaymentMethodTotal>> MonthlyTotalsAsync(string ownerId, DateTime month, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId).OrderBy(m => m.Name).ToListAsync(cancellationToken);
        var rows = await new ExpenseFilter(Month: month).ApplyTo(db.Expenses.AsNoTracking().Where(e => e.OwnerId == ownerId))
            .Select(e => new { e.PaymentMethodId, e.Amount }).ToListAsync(cancellationToken);
        var result = methods.Select(m => new PaymentMethodTotal(m.Id, m.Name, m.Type,
            rows.Where(e => e.PaymentMethodId == m.Id).Sum(e => (decimal)e.Amount), rows.Count(e => e.PaymentMethodId == m.Id))).ToList();
        result.Add(new(null, "미지정", "", rows.Where(e => e.PaymentMethodId == null).Sum(e => (decimal)e.Amount), rows.Count(e => e.PaymentMethodId == null)));
        return result;
    }
}
