using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public enum TagRenameResult { Renamed, Merged, NotFound, Conflict }

public sealed class TagService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    // 태그와 사용 건수·금액을 이름순으로 돌려줍니다. 지출이 하나도 없는 태그도 포함합니다.
    public async Task<List<TagUsage>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Tags.AsNoTracking().Where(tag => tag.OwnerId == ownerId)
            .Select(tag => new { tag.Id, tag.Name, Count = tag.ExpenseLinks.Count(), Amount = tag.ExpenseLinks.Sum(link => (long?)link.Expense!.Amount) ?? 0 })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(row => row.Name, StringComparer.Ordinal).Select(row => new TagUsage(row.Id, row.Name, row.Count, row.Amount)).ToList();
    }

    // 지출에 붙일 태그를 맞춥니다. 이름이 이미 있으면(대소문자 무시) 기존 태그를 쓰고 없으면 만듭니다.
    // 호출하는 쪽의 트랜잭션 안에서 쓰며, 저장은 호출하는 쪽이 합니다.
    internal static async Task ApplyAsync(ExpensesDbContext db, string ownerId, ExpenseRecord expense, IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var wanted = Normalize(names);
        var byKey = await EnsureAsync(db, ownerId, wanted, cancellationToken);
        var keys = wanted.Select(TagNames.Key).ToList();

        var target = keys.ToHashSet();
        foreach (var link in expense.TagLinks.Where(link => link.Tag is null || !target.Contains(link.Tag.NormalizedName)).ToList())
        {
            expense.TagLinks.Remove(link);
            if (db.Entry(link).State != EntityState.Detached && db.Entry(link).State != EntityState.Added) db.ExpenseTags.Remove(link);
        }
        var present = expense.TagLinks.Where(link => link.Tag is not null).Select(link => link.Tag!.NormalizedName).ToHashSet();
        foreach (var name in wanted.Where(name => !present.Contains(TagNames.Key(name))))
            expense.TagLinks.Add(new ExpenseTag { Expense = expense, Tag = byKey[TagNames.Key(name)] });
    }

    // 이름에 해당하는 태그를 돌려줍니다(대소문자 무시). 없는 이름은 새로 추가하되 저장은 호출하는 쪽이 합니다.
    // 이미 있는 태그의 표시 이름은 바꾸지 않습니다. 계정당 태그 개수 제한을 넘으면 아무것도 추가하지 않고 예외를 던집니다.
    internal static async Task<Dictionary<string, Tag>> EnsureAsync(ExpensesDbContext db, string ownerId, IEnumerable<string> names,
        CancellationToken cancellationToken)
    {
        var wanted = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in names)
        {
            var name = TagNames.Clean(raw) ?? throw new ArgumentException($"태그는 {TagNames.MaxLength}자 이하이며 쉼표·세미콜론을 쓸 수 없습니다: ‘{raw}’");
            if (seen.Add(TagNames.Key(name))) wanted.Add(name);
        }
        var keys = wanted.Select(TagNames.Key).ToList();
        var existing = keys.Count == 0 ? [] : await db.Tags.Where(tag => tag.OwnerId == ownerId && keys.Contains(tag.NormalizedName)).ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(tag => tag.NormalizedName);
        var created = wanted.Where(name => !byKey.ContainsKey(TagNames.Key(name))).ToList();
        if (created.Count > 0 && await db.Tags.CountAsync(tag => tag.OwnerId == ownerId, cancellationToken) + created.Count > TagNames.MaxPerOwner)
            throw new ArgumentException($"태그는 최대 {TagNames.MaxPerOwner}개까지 만들 수 있습니다. 태그 관리에서 쓰지 않는 태그를 지워 주세요.");
        foreach (var name in created)
        {
            var tag = new Tag { OwnerId = ownerId, Name = name, NormalizedName = TagNames.Key(name) };
            db.Tags.Add(tag);
            byKey[tag.NormalizedName] = tag;
        }
        return byKey;
    }

    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string>? names)
    {
        var result = new List<string>();
        var keys = new HashSet<string>();
        foreach (var raw in names ?? [])
        {
            var name = TagNames.Clean(raw) ?? throw new ArgumentException($"태그는 {TagNames.MaxLength}자 이하이며 쉼표·세미콜론을 쓸 수 없습니다: ‘{raw}’");
            if (keys.Add(TagNames.Key(name))) result.Add(name);
        }
        if (result.Count > TagNames.MaxPerExpense) throw new ArgumentException($"태그는 지출 하나에 {TagNames.MaxPerExpense}개까지 붙일 수 있습니다.");
        return result;
    }

    // 이름을 바꿉니다. 같은 이름의 태그가 이미 있으면 mergeIfExists일 때만 그 태그로 합치고, 아니면 Conflict입니다.
    public async Task<TagRenameResult> RenameAsync(string ownerId, int id, string newName, bool mergeIfExists = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var name = TagNames.Clean(newName) ?? throw new ArgumentException($"태그는 {TagNames.MaxLength}자 이하이며 쉼표·세미콜론을 쓸 수 없습니다.");
        var key = TagNames.Key(name);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tag = await db.Tags.SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.Id == id, cancellationToken);
        if (tag is null) return TagRenameResult.NotFound;
        var other = await db.Tags.SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.NormalizedName == key && item.Id != id, cancellationToken);
        if (other is null)
        {
            tag.Name = name;
            tag.NormalizedName = key;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TagRenameResult.Renamed;
        }
        if (!mergeIfExists) return TagRenameResult.Conflict;

        // 이 태그가 붙은 지출을 other로 옮기고, 이미 other가 붙은 지출은 중복 없이 둡니다.
        var movedExpenseIds = await db.ExpenseTags.Where(link => link.TagId == id).Select(link => link.ExpenseId).ToListAsync(cancellationToken);
        var alreadyTagged = (await db.ExpenseTags.Where(link => link.TagId == other.Id && movedExpenseIds.Contains(link.ExpenseId))
            .Select(link => link.ExpenseId).ToListAsync(cancellationToken)).ToHashSet();
        await db.ExpenseTags.Where(link => link.TagId == id).ExecuteDeleteAsync(cancellationToken);
        db.ExpenseTags.AddRange(movedExpenseIds.Where(expenseId => !alreadyTagged.Contains(expenseId))
            .Select(expenseId => new ExpenseTag { ExpenseId = expenseId, TagId = other.Id }));
        await db.SaveChangesAsync(cancellationToken);
        await db.Tags.Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TagRenameResult.Merged;
    }

    // 태그를 지웁니다. 지출은 그대로 두고 붙어 있던 태그만 사라집니다.
    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Tags.AnyAsync(tag => tag.OwnerId == ownerId && tag.Id == id, cancellationToken)) return false;
        await db.ExpenseTags.Where(link => link.TagId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Tags.Where(tag => tag.OwnerId == ownerId && tag.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
