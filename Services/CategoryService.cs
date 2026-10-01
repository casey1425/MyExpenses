using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;

namespace MyExpenses.Services;

// 기존 문자열 카테고리를 유지해 DB 재작성 없이 업그레이드합니다.
// 이름 변경은 모든 참조를 같은 트랜잭션에서 변경합니다.
public sealed class CategoryService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<UserCategory>> ListAsync(string ownerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureAsync(db, ownerId, ct);
        return await db.UserCategories.AsNoTracking().Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(ct);
    }

    internal static async Task EnsureAsync(ExpensesDbContext db, string ownerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (await db.UserCategories.AnyAsync(c => c.OwnerId == ownerId, ct)) return;
        var historical = await db.Expenses.Where(e => e.OwnerId == ownerId).Select(e => e.Category)
            .Union(db.CategoryBudgets.Where(e => e.OwnerId == ownerId).Select(e => e.Category))
            .Union(db.ExpenseTemplates.Where(e => e.OwnerId == ownerId).Select(e => e.Category))
            .Union(db.RecurringExpenseRules.Where(e => e.OwnerId == ownerId).Select(e => e.Category)).ToListAsync(ct);
        var names = ExpenseCategories.All.Concat(historical.Order()).Distinct(StringComparer.Ordinal).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT OR IGNORE INTO "UserCategories" ("OwnerId", "Name", "Position", "IsArchived") VALUES ({ownerId}, {name}, {i}, 0)""", ct);
        }
    }

    internal static async Task VerifyAsync(ExpensesDbContext db, string ownerId, string name,
        bool allowArchived = false, CancellationToken ct = default)
    {
        await EnsureAsync(db, ownerId, ct);
        if (!await db.UserCategories.AnyAsync(c => c.OwnerId == ownerId && c.Name == name &&
                (allowArchived || !c.IsArchived), ct))
            throw new ArgumentException("카테고리가 없거나 보관 중입니다. 카테고리 목록을 새로고침해 주세요.");
    }

    public async Task SaveAsync(string ownerId, int? id, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        name = name.Trim();
        if (name.Length is < 1 or > 30 || name.Any(char.IsControl))
            throw new ArgumentException("카테고리 이름은 제어문자 없이 1~30자로 입력해 주세요.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await EnsureAsync(db, ownerId, ct);
        if (await db.UserCategories.AnyAsync(c => c.OwnerId == ownerId && c.Name == name && c.Id != id, ct))
            throw new ArgumentException("보관된 항목을 포함해 같은 이름의 카테고리가 이미 있습니다.");
        if (id is null)
        {
            var position = await db.UserCategories.Where(c => c.OwnerId == ownerId).MaxAsync(c => c.Position, ct) + 1;
            db.UserCategories.Add(new UserCategory { OwnerId = ownerId, Name = name, Position = position });
        }
        else
        {
            var category = await db.UserCategories.SingleOrDefaultAsync(c => c.OwnerId == ownerId && c.Id == id, ct)
                ?? throw new ArgumentException("수정할 카테고리가 없습니다.");
            var old = category.Name;
            if (old != name)
            {
                await db.Expenses.Where(e => e.OwnerId == ownerId && e.Category == old)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Category, name), ct);
                await db.CategoryBudgets.Where(e => e.OwnerId == ownerId && e.Category == old)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Category, name), ct);
                await db.ExpenseTemplates.Where(e => e.OwnerId == ownerId && e.Category == old)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Category, name), ct);
                await db.RecurringExpenseRules.Where(e => e.OwnerId == ownerId && e.Category == old)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.Category, name), ct);
                category.Name = name;
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ArchiveAsync(string ownerId, int id, bool archived, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var category = await db.UserCategories.SingleOrDefaultAsync(c => c.OwnerId == ownerId && c.Id == id, ct)
            ?? throw new ArgumentException("카테고리가 없습니다.");
        if (archived && !category.IsArchived &&
            await db.UserCategories.CountAsync(c => c.OwnerId == ownerId && !c.IsArchived, ct) <= 1)
            throw new ArgumentException("사용 중인 카테고리를 최소 하나 남겨 주세요.");
        category.IsArchived = archived;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task MoveAsync(string ownerId, int id, int direction, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (direction is not (-1 or 1)) throw new ArgumentException("순서 변경 방향이 올바르지 않습니다.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var list = await db.UserCategories.Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(ct);
        var index = list.FindIndex(c => c.Id == id);
        if (index < 0) throw new ArgumentException("카테고리가 없습니다.");
        var target = index + direction;
        if (target >= 0 && target < list.Count) (list[index], list[target]) = (list[target], list[index]);
        for (var i = 0; i < list.Count; i++) list[i].Position = i;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
