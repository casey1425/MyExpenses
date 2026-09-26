using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

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
        var known = await ExistingKeysAsync(db, ownerId, rows, cancellationToken);
        return rows.Select(row => new ExpenseCsvCandidate(row, !known.Add(Key(row)))).ToList();
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
        HashSet<ExpenseKey> known = includeDuplicates
            ? []
            : await ExistingKeysAsync(db, ownerId, rows, cancellationToken);

        var imported = 0;
        foreach (var row in rows)
        {
            if (!includeDuplicates && !known.Add(Key(row)))
                continue;

            db.Expenses.Add(new ExpenseRecord
            {
                OwnerId = ownerId,
                Date = row.Date,
                Amount = row.Amount,
                Category = row.Category,
                Memo = row.Memo
            });
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
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

    private static ExpenseKey Key(ExpenseCsvRow row) =>
        new(row.Date.Date, row.Amount, row.Category, row.Memo);

    private static ExpenseKey Key(ExpenseRecord expense) =>
        new(expense.Date.Date, expense.Amount, expense.Category, expense.Memo);

    private sealed record ExpenseKey(DateTime Date, long Amount, string Category, string Memo);
}
