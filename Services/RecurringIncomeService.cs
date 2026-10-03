using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class RecurringIncomeService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public static DateTime KoreanToday => KoreanClock.Today;

    public async Task<List<RecurringIncomeRule>> ListAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.RecurringIncomeRules.AsNoTracking()
            .Where(rule => rule.OwnerId == ownerId)
            .OrderBy(rule => rule.DayOfMonth).ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task CreateAsync(string ownerId, DateTime today, int dayOfMonth, long amount, string source, string memo,
        CancellationToken cancellationToken = default)
    {
        Validate(ownerId, dayOfMonth, amount, source, memo);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.RecurringIncomeRules.Add(new RecurringIncomeRule
        {
            OwnerId = ownerId,
            StartMonth = MonthStart(today),
            DayOfMonth = dayOfMonth,
            Amount = amount,
            Source = source,
            Memo = memo.Trim()
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(string ownerId, int id, int dayOfMonth, long amount, string source, string memo,
        CancellationToken cancellationToken = default)
    {
        Validate(ownerId, dayOfMonth, amount, source, memo);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.RecurringIncomeRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId, cancellationToken);
        if (rule is null)
            return false;

        rule.DayOfMonth = dayOfMonth;
        rule.Amount = amount;
        rule.Source = source;
        rule.Memo = memo.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetActiveAsync(string ownerId, int id, bool isActive, DateTime today,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.RecurringIncomeRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId, cancellationToken);
        if (rule is null)
            return false;

        if (isActive && !rule.IsActive)
            rule.StartMonth = MonthStart(today); // 중지 기간의 수입은 소급 생성하지 않습니다.
        rule.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rule = await db.RecurringIncomeRules.SingleOrDefaultAsync(item => item.Id == id && item.OwnerId == ownerId, cancellationToken);
        if (rule is null)
            return false;

        await db.RecurringIncomeOccurrences
            .Where(item => item.RuleId == id && item.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        db.RecurringIncomeRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // 도래했지만 아직 기록하지 않은 월의 수입을 만듭니다. 같은 규칙·같은 달은 한 번만 처리합니다.
    public async Task<int> GenerateDueAsync(string ownerId, DateTime today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        today = today.Date;
        var currentMonth = MonthStart(today);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rules = await db.RecurringIncomeRules.AsNoTracking()
            .Where(rule => rule.OwnerId == ownerId && rule.IsActive && rule.StartMonth <= currentMonth)
            .ToListAsync(cancellationToken);
        if (rules.Count == 0)
            return 0;

        var ruleIds = rules.Select(rule => rule.Id).ToList();
        var handled = (await db.RecurringIncomeOccurrences.AsNoTracking()
                .Where(item => item.OwnerId == ownerId && ruleIds.Contains(item.RuleId))
                .Select(item => new { item.RuleId, item.Month })
                .ToListAsync(cancellationToken))
            .Select(item => (item.RuleId, item.Month))
            .ToHashSet();

        var count = 0;
        foreach (var rule in rules)
        {
            for (var month = rule.StartMonth; month <= currentMonth; month = month.AddMonths(1))
            {
                // 29~31일이 없는 달은 말일에 기록합니다.
                var dueDate = new DateTime(month.Year, month.Month,
                    Math.Min(rule.DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
                if (dueDate > today || !handled.Add((rule.Id, month)))
                    continue;

                db.Incomes.Add(new IncomeRecord
                {
                    OwnerId = ownerId,
                    Date = dueDate,
                    Amount = rule.Amount,
                    Source = rule.Source,
                    Memo = rule.Memo
                });
                db.RecurringIncomeOccurrences.Add(new RecurringIncomeOccurrence
                {
                    RuleId = rule.Id,
                    Month = month,
                    OwnerId = ownerId
                });
                count++;
            }
        }

        if (count > 0)
            await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1);

    private static void Validate(string ownerId, int dayOfMonth, long amount, string source, string memo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (dayOfMonth is < 1 or > 31)
            throw new ArgumentException("지정일은 1~31일 사이로 입력해 주세요.", nameof(dayOfMonth));
        if (amount <= 0)
            throw new ArgumentException("금액은 1원 이상으로 입력해 주세요.", nameof(amount));
        if (!IncomeRecord.Sources.Contains(source))
            throw new ArgumentException("수입 분류를 선택해 주세요.", nameof(source));
        if (memo is null || memo.Trim().Length > 100)
            throw new ArgumentException("메모는 100자 이하여야 합니다.", nameof(memo));
    }
}
