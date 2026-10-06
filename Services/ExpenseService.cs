using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;

namespace MyExpenses.Services;

// Tags가 null이면 수정할 때 기존 태그를 그대로 두고, 빈 목록이면 모두 지웁니다. 추가할 때 null은 태그 없음입니다.
public sealed record ExpenseInput(DateTime Date, long Amount, string Category, string Memo, int? PaymentMethodId = null,
    IReadOnlyList<string>? Tags = null);

// 자주 쓴 메모와 그 메모로 가장 최근에 기록한 카테고리·결제수단·금액입니다. 입력 자동 완성에 씁니다.
public sealed record MemoSuggestion(string Memo, string Category, int? PaymentMethodId, long Amount, int Count);

public sealed class ExpenseService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<ExpenseRecord>> ListAsync(string ownerId, ExpenseFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (filter.Category is not null) await CategoryService.VerifyAsync(db, ownerId, filter.Category, true, cancellationToken);
        var query = db.Expenses.AsNoTracking().Include(e => e.PaymentMethod).Include(e => e.TagLinks).ThenInclude(l => l.Tag)
            .AsSplitQuery().Where(e => e.OwnerId == ownerId);
        return await filter.Order(filter.ApplyTo(query)).ToListAsync(cancellationToken);
    }

    public const int SuggestionLimit = 30;
    public const int SuggestionDays = 365;

    // 최근 1년 기록에서 많이 쓴 메모 순으로 돌려줍니다. 대소문자와 앞뒤 공백은 같은 메모로 봅니다.
    public async Task<List<MemoSuggestion>> SuggestionsAsync(string ownerId, DateTime today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var since = today.Date.AddDays(-SuggestionDays);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Expenses.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.Memo != "" && e.Date >= since && e.Date <= today.Date)
            .Select(e => new { e.Id, e.Date, e.Memo, e.Category, e.PaymentMethodId, e.Amount })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(e => e.Memo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).First();
                return new MemoSuggestion(latest.Memo.Trim(), latest.Category, latest.PaymentMethodId, latest.Amount, g.Count());
            })
            .OrderByDescending(m => m.Count).ThenBy(m => m.Memo, StringComparer.Ordinal)
            .Take(SuggestionLimit).ToList();
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
        await CategoryService.VerifyAsync(db, ownerId, input.Category, false, cancellationToken);
        var expense = new ExpenseRecord { OwnerId = ownerId };
        Apply(expense, input);
        db.Expenses.Add(expense);
        await TagService.ApplyAsync(db, ownerId, expense, input.Tags ?? [], cancellationToken);
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
        var expense = await db.Expenses.Include(e => e.TagLinks).ThenInclude(l => l.Tag)
            .SingleOrDefaultAsync(e => e.OwnerId == ownerId && e.Id == id, cancellationToken);
        if (expense is null) return null;
        await CategoryService.VerifyAsync(db, ownerId, input.Category, expense.Category == input.Category, cancellationToken);
        Apply(expense, input);
        if (input.Tags is not null) await TagService.ApplyAsync(db, ownerId, expense, input.Tags, cancellationToken);
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
        if (string.IsNullOrWhiteSpace(input.Category) || input.Category.Length > 30 || input.Memo is null || input.Memo.Trim().Length > 100)
            throw new ArgumentException("카테고리와 100자 이하 메모를 확인해 주세요.");
        TagService.Normalize(input.Tags);
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
