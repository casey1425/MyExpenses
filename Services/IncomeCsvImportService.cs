using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record IncomeCsvCandidate(IncomeCsvRow Row, bool IsDuplicate);

public sealed class IncomeCsvImportService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    private sealed record IncomeKey(DateTime Date, long Amount, string Source, string Memo);

    public async Task<IReadOnlyList<IncomeCsvCandidate>> PreviewAsync(
        string ownerId, IReadOnlyList<IncomeCsvRow> rows, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (rows.Count == 0)
            return [];

        Validate(rows);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var known = await ExistingKeysAsync(db, ownerId, rows, cancellationToken);
        // 파일 안에서 먼저 나온 행과 같은 행도 중복 후보로 표시합니다.
        return rows.Select(row => new IncomeCsvCandidate(row, !known.Add(Key(row)))).ToList();
    }

    public async Task<int> ImportAsync(
        string ownerId, IReadOnlyList<IncomeCsvRow> rows, bool includeDuplicates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (rows.Count is < 1 or > ExpenseCsvImporter.MaxRows)
            throw new ArgumentOutOfRangeException(nameof(rows));

        Validate(rows);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        HashSet<IncomeKey> known = includeDuplicates
            ? []
            : await ExistingKeysAsync(db, ownerId, rows, cancellationToken);

        var imported = 0;
        foreach (var row in rows)
        {
            if (!includeDuplicates && !known.Add(Key(row)))
                continue;

            db.Incomes.Add(new IncomeRecord
            {
                OwnerId = ownerId,
                Date = row.Date,
                Amount = row.Amount,
                Source = row.Source,
                Memo = row.Memo
            });
            imported++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return imported;
    }

    private static void Validate(IReadOnlyList<IncomeCsvRow> rows)
    {
        foreach (var row in rows)
            if (row.Date == default || row.Amount <= 0 || !IncomeRecord.Sources.Contains(row.Source) || row.Memo.Trim().Length > 100)
                throw new ArgumentException($"{row.RowNumber}번째 행: 날짜·금액·분류·메모를 확인해 주세요.");
    }

    private static async Task<HashSet<IncomeKey>> ExistingKeysAsync(
        ExpensesDbContext db, string ownerId, IReadOnlyList<IncomeCsvRow> rows, CancellationToken cancellationToken)
    {
        var firstDate = rows.Min(row => row.Date);
        var lastDate = rows.Max(row => row.Date);
        var existing = await db.Incomes.AsNoTracking()
            .Where(income => income.OwnerId == ownerId && income.Date >= firstDate && income.Date <= lastDate)
            .ToListAsync(cancellationToken);
        return existing.Select(income => new IncomeKey(income.Date.Date, income.Amount, income.Source, income.Memo)).ToHashSet();
    }

    private static IncomeKey Key(IncomeCsvRow row) => new(row.Date.Date, row.Amount, row.Source, row.Memo.Trim());
}
