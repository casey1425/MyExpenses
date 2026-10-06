using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record ExpenseCsvCandidate(ExpenseCsvRow Row, bool IsDuplicate);

public sealed class ExpenseCsvImportService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<IReadOnlyList<ExpenseCsvCandidate>> PreviewAsync(
        string ownerId, IReadOnlyList<ExpenseCsvRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (rows.Count == 0)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ValidateRowsAsync(db, ownerId, rows, cancellationToken);
        var methods = await ResolveMethodsAsync(db, ownerId, rows, cancellationToken);
        var known = await ExistingKeysAsync(db, ownerId, rows, cancellationToken);
        return rows.Select(row => new ExpenseCsvCandidate(row, !known.Add(Key(row, methods[(row.PaymentMethodName, row.PaymentMethodType)])))).ToList();
    }

    public async Task<int> ImportAsync(
        string ownerId, IReadOnlyList<ExpenseCsvRow> rows, bool includeDuplicates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (rows.Count is < 1 or > ExpenseCsvImporter.MaxRows)
            throw new ArgumentOutOfRangeException(nameof(rows));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ValidateRowsAsync(db, ownerId, rows, cancellationToken);
        var methods = await ResolveMethodsAsync(db, ownerId, rows, cancellationToken);
        HashSet<ExpenseKey> known = includeDuplicates
            ? []
            : await ExistingKeysAsync(db, ownerId, rows, cancellationToken);

        var tagsByKey = await TagService.EnsureAsync(db, ownerId, rows.SelectMany(row => row.Tags ?? []), cancellationToken);
        var imported = 0;
        foreach (var row in rows)
        {
            var methodId = methods[(row.PaymentMethodName, row.PaymentMethodType)];
            if (!includeDuplicates && !known.Add(Key(row, methodId)))
                continue;

            var expense = new ExpenseRecord
            {
                OwnerId = ownerId,
                Date = row.Date,
                Amount = row.Amount,
                Category = row.Category,
                Memo = row.Memo,
                PaymentMethodId = methodId
            };
            foreach (var tag in row.Tags ?? [])
                expense.TagLinks.Add(new ExpenseTag { Expense = expense, Tag = tagsByKey[TagNames.Key(tag)] });
            db.Expenses.Add(expense);
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
    }

    private static async Task ValidateRowsAsync(ExpensesDbContext db, string ownerId,
        IReadOnlyList<ExpenseCsvRow> rows, CancellationToken cancellationToken)
    {
        await CategoryService.EnsureAsync(db, ownerId, cancellationToken);
        var categories = await db.UserCategories.Where(c => c.OwnerId == ownerId && !c.IsArchived)
            .Select(c => c.Name).ToListAsync(cancellationToken);
        foreach (var row in rows)
            if (!categories.Contains(row.Category) || row.Date == default || row.Amount <= 0 || row.Memo.Length > 100)
                throw new ArgumentException($"{row.RowNumber}번째 행: 날짜·금액·메모와 사용 중인 내 카테고리를 확인해 주세요.");
    }

    private static async Task<HashSet<ExpenseKey>> ExistingKeysAsync(
        ExpensesDbContext db, string ownerId, IReadOnlyList<ExpenseCsvRow> rows,
        CancellationToken cancellationToken)
    {
        var firstDate = rows.Min(row => row.Date);
        var lastDate = rows.Max(row => row.Date);
        var existing = await db.Expenses.AsNoTracking()
            .Where(expense => expense.OwnerId == ownerId &&
                              expense.Date >= firstDate && expense.Date <= lastDate)
            .ToListAsync(cancellationToken);
        return existing.Select(Key).ToHashSet();
    }

    private static async Task<Dictionary<(string, string), int?>> ResolveMethodsAsync(
        ExpensesDbContext db, string ownerId, IReadOnlyList<ExpenseCsvRow> rows, CancellationToken cancellationToken)
    {
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId).ToListAsync(cancellationToken);
        var result = new Dictionary<(string, string), int?> { [("", "")] = null };
        foreach (var row in rows)
        {
            var key = (row.PaymentMethodName, row.PaymentMethodType);
            if (result.ContainsKey(key)) continue;
            var method = methods.SingleOrDefault(m => m.Name == row.PaymentMethodName && m.Type == row.PaymentMethodType);
            if (method is null) throw new ArgumentException($"{row.RowNumber}번째 행: 결제수단 ‘{row.PaymentMethodName}’ ({row.PaymentMethodType})을 내 계정에 먼저 등록해 주세요.");
            result[key] = method.Id;
        }
        return result;
    }

    private static ExpenseKey Key(ExpenseCsvRow row, int? methodId) =>
        new(row.Date.Date, row.Amount, row.Category, row.Memo, methodId);

    private static ExpenseKey Key(ExpenseRecord expense) =>
        new(expense.Date.Date, expense.Amount, expense.Category, expense.Memo, expense.PaymentMethodId);

    private sealed record ExpenseKey(DateTime Date, long Amount, string Category, string Memo, int? PaymentMethodId);
}
