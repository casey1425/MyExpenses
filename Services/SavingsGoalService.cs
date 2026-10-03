using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed record SavingsGoalSummary(SavingsGoal Goal, SavingsGoalProgress Progress);

public sealed class SavingsGoalService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public const int MaxGoals = 20;
    public const long MaxAmount = 1_000_000_000_000;
    private static readonly DateTime MinDate = new(2000, 1, 1);
    private static readonly DateTime MaxDate = new(2100, 12, 31);

    public async Task<List<SavingsGoalSummary>> ListAsync(string ownerId, DateTime today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var goals = await db.SavingsGoals.AsNoTracking().Where(goal => goal.OwnerId == ownerId)
            .OrderBy(goal => goal.CreatedDate).ThenBy(goal => goal.Id).ToListAsync(cancellationToken);
        var deposits = await db.SavingsDeposits.AsNoTracking().Where(deposit => deposit.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var byGoal = deposits.ToLookup(deposit => deposit.GoalId);
        return goals.Select(goal => new SavingsGoalSummary(goal,
            SavingsGoalCalculator.Calculate(goal, byGoal[goal.Id].ToList(), today))).ToList();
    }

    public async Task<SavingsGoal> CreateAsync(string ownerId, DateTime today, string name, long targetAmount,
        DateTime? targetDate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        name = ValidateGoal(name, targetAmount, targetDate, today, checkTargetDate: true);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.SavingsGoals.CountAsync(goal => goal.OwnerId == ownerId, cancellationToken) >= MaxGoals)
            throw new ArgumentException($"목표는 최대 {MaxGoals}개까지 만들 수 있습니다. 필요 없는 목표를 삭제해 주세요.");
        if (await db.SavingsGoals.AnyAsync(goal => goal.OwnerId == ownerId && goal.Name == name, cancellationToken))
            throw new ArgumentException("같은 이름의 목표가 있습니다. 다른 이름을 입력해 주세요.");

        var created = new SavingsGoal
        {
            OwnerId = ownerId,
            Name = name,
            TargetAmount = targetAmount,
            TargetDate = targetDate?.Date,
            CreatedDate = today.Date
        };
        db.SavingsGoals.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<bool> UpdateAsync(string ownerId, int id, DateTime today, string name, long targetAmount,
        DateTime? targetDate, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(item => item.OwnerId == ownerId && item.Id == id, cancellationToken);
        if (goal is null)
            return false;

        // 목표일을 바꾸지 않는 수정은 이미 지난 목표일이어도 허용합니다.
        var dateChanged = goal.TargetDate?.Date != targetDate?.Date;
        name = ValidateGoal(name, targetAmount, targetDate, today, checkTargetDate: dateChanged);
        if (await db.SavingsGoals.AnyAsync(item => item.OwnerId == ownerId && item.Name == name && item.Id != id, cancellationToken))
            throw new ArgumentException("같은 이름의 목표가 있습니다. 다른 이름을 입력해 주세요.");

        goal.Name = name;
        goal.TargetAmount = targetAmount;
        goal.TargetDate = targetDate?.Date;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // 목표를 지우면 저축 내역도 함께 삭제합니다.
    public async Task<bool> DeleteAsync(string ownerId, int id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.SavingsGoals.AnyAsync(goal => goal.OwnerId == ownerId && goal.Id == id, cancellationToken))
            return false;

        await db.SavingsDeposits.Where(deposit => deposit.OwnerId == ownerId && deposit.GoalId == id)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SavingsGoals.Where(goal => goal.OwnerId == ownerId && goal.Id == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<List<SavingsDeposit>> ListDepositsAsync(string ownerId, int goalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavingsDeposits.AsNoTracking()
            .Where(deposit => deposit.OwnerId == ownerId && deposit.GoalId == goalId)
            .OrderByDescending(deposit => deposit.Date).ThenByDescending(deposit => deposit.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<SavingsDeposit> AddDepositAsync(string ownerId, int goalId, DateTime today, DateTime date, long amount,
        string memo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (date == default || date.Date < MinDate || date.Date > today.Date)
            throw new ArgumentException("저축한 날짜는 오늘 이전으로 입력해 주세요.");
        if (amount < 1 || amount > MaxAmount)
            throw new ArgumentException("저축 금액은 1원 이상으로 입력해 주세요.");
        if (memo is null || memo.Trim().Length > 100)
            throw new ArgumentException("메모는 100자 이하여야 합니다.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // 같은 트랜잭션에서 소유자를 확인해 타 계정·삭제된 목표에 저축이 쌓이지 않게 합니다.
        if (!await db.SavingsGoals.AnyAsync(goal => goal.OwnerId == ownerId && goal.Id == goalId, cancellationToken))
            throw new ArgumentException("목표가 삭제되었거나 사용할 수 없습니다. 목록을 새로고침해 주세요.");

        var deposit = new SavingsDeposit { OwnerId = ownerId, GoalId = goalId, Date = date.Date, Amount = amount, Memo = memo.Trim() };
        db.SavingsDeposits.Add(deposit);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deposit;
    }

    public async Task<bool> DeleteDepositAsync(string ownerId, int depositId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavingsDeposits.Where(deposit => deposit.OwnerId == ownerId && deposit.Id == depositId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
    }

    // 직전 3개월(이번 달 제외)의 월평균 순수지입니다. 수입·지출 기록이 모두 없으면 null입니다.
    public async Task<long?> AverageMonthlyNetAsync(string ownerId, DateTime today, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var end = new DateTime(today.Year, today.Month, 1);
        var start = end.AddMonths(-3);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var incomes = await db.Incomes.AsNoTracking()
            .Where(income => income.OwnerId == ownerId && income.Date >= start && income.Date < end)
            .Select(income => income.Amount).ToListAsync(cancellationToken);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(expense => expense.OwnerId == ownerId && expense.Date >= start && expense.Date < end)
            .Select(expense => expense.Amount).ToListAsync(cancellationToken);
        if (incomes.Count == 0 && expenses.Count == 0)
            return null;
        return (incomes.Sum() - expenses.Sum()) / 3;
    }

    private static string ValidateGoal(string name, long targetAmount, DateTime? targetDate, DateTime today, bool checkTargetDate)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 50)
            throw new ArgumentException("목표 이름은 1~50자로 입력해 주세요.");
        if (targetAmount < 1 || targetAmount > MaxAmount)
            throw new ArgumentException("목표 금액은 1원 이상으로 입력해 주세요.");
        if (targetDate is DateTime date)
        {
            if (date.Date < MinDate || date.Date > MaxDate)
                throw new ArgumentException("목표일은 2000년부터 2100년 사이로 입력해 주세요.");
            if (checkTargetDate && date.Date < today.Date)
                throw new ArgumentException("목표일은 오늘 이후로 입력해 주세요.");
        }
        return name;
    }
}
