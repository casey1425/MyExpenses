using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class RecurringExpenseService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task<List<RecurringExpenseRule>> ListAsync(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.RecurringExpenseRules.AsNoTracking()
            .Where(rule => rule.OwnerId == ownerId)
            .OrderBy(rule => rule.DayOfMonth)
            .ThenBy(rule => rule.Id)
            .ToListAsync();
    }

    public async Task CreateAsync(string ownerId, int dayOfMonth, long amount, string category, string memo)
    {
        Validate(ownerId, dayOfMonth, amount, category, memo);
        var today = DateTime.Today;
        await using var db = await dbFactory.CreateDbContextAsync();
        db.RecurringExpenseRules.Add(new RecurringExpenseRule
        {
            OwnerId = ownerId,
            StartMonth = MonthStart(today),
            DayOfMonth = dayOfMonth,
            Amount = amount,
            Category = category,
            Memo = memo.Trim()
        });
        await db.SaveChangesAsync();
    }

    public async Task<bool> UpdateAsync(string ownerId, int id, int dayOfMonth, long amount, string category, string memo)
    {
        Validate(ownerId, dayOfMonth, amount, category, memo);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rule = await db.RecurringExpenseRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId);
        if (rule is null)
            return false;

        rule.DayOfMonth = dayOfMonth;
        rule.Amount = amount;
        rule.Category = category;
        rule.Memo = memo.Trim();
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetActiveAsync(string ownerId, int id, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rule = await db.RecurringExpenseRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId);
        if (rule is null)
            return false;

        if (isActive && !rule.IsActive)
            rule.StartMonth = MonthStart(DateTime.Today); // 중지 기간의 지출은 소급 생성하지 않습니다.
        rule.IsActive = isActive;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var rule = await db.RecurringExpenseRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId);
        if (rule is null)
            return false;

        await db.RecurringExpenseOccurrences
            .Where(item => item.RuleId == id && item.OwnerId == ownerId)
            .ExecuteDeleteAsync();
        db.RecurringExpenseRules.Remove(rule);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<int> GenerateDueAsync(string ownerId, DateTime today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        today = today.Date;
        var currentMonth = MonthStart(today);
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var rules = await db.RecurringExpenseRules.AsNoTracking()
            .Where(rule => rule.OwnerId == ownerId && rule.IsActive && rule.StartMonth <= currentMonth)
            .ToListAsync();
        if (rules.Count == 0)
            return 0;

        var ruleIds = rules.Select(rule => rule.Id).ToList();
        var handled = (await db.RecurringExpenseOccurrences.AsNoTracking()
                .Where(item => item.OwnerId == ownerId && ruleIds.Contains(item.RuleId))
                .Select(item => new { item.RuleId, item.Month })
                .ToListAsync())
            .Select(item => (item.RuleId, item.Month))
            .ToHashSet();

        var count = 0;
        foreach (var rule in rules)
        {
            for (var month = rule.StartMonth; month <= currentMonth; month = month.AddMonths(1))
            {
                var dueDate = new DateTime(month.Year, month.Month,
                    Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
                if (dueDate > today || !handled.Add((rule.Id, month)))
                    continue;

                db.Expenses.Add(new ExpenseRecord
                {
                    OwnerId = ownerId,
                    Date = dueDate,
                    Amount = rule.Amount,
                    Category = rule.Category,
                    Memo = rule.Memo
                });
                db.RecurringExpenseOccurrences.Add(new RecurringExpenseOccurrence
                {
                    RuleId = rule.Id,
                    Month = month,
                    OwnerId = ownerId
                });
                count++;
            }
        }

        if (count > 0)
            await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return count;
    }

    private static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1);

    private static void Validate(string ownerId, int dayOfMonth, long amount, string category, string memo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (dayOfMonth is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (!ExpenseCategories.IsSupported(category))
            throw new ArgumentException("지원하지 않는 카테고리입니다.", nameof(category));
        if (memo is null || memo.Trim().Length > 100)
            throw new ArgumentException("메모는 100자 이하여야 합니다.", nameof(memo));
    }
}
