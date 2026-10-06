using System.Globalization;
using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public enum BackupRestoreMode
{
    // 백업에만 있는 항목을 추가합니다. 이미 있는 항목은 건너뛰고 기존 데이터는 바꾸지 않습니다.
    Merge,
    // 현재 데이터를 모두 지우고 백업 내용으로 교체합니다.
    Replace
}

public sealed record BackupSectionReport(string Name, int Total, int Added, int Skipped);

public sealed record BackupRestoreReport(BackupRestoreMode Mode, bool DryRun, IReadOnlyList<BackupSectionReport> Sections,
    IReadOnlyList<string> Warnings, int DeletedRows)
{
    public int TotalAdded => Sections.Sum(section => section.Added);
    public int TotalSkipped => Sections.Sum(section => section.Skipped);
}

public sealed class BackupService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    private static string MonthText(DateTime month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private static DateTime Month(string value) => BackupValidator.TryParseMonth(value, out var month)
        ? month : throw new ArgumentException("월 형식이 올바르지 않습니다.");
    private static DateTime Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public async Task<BackupFile> ExportAsync(string ownerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // 한 트랜잭션에서 읽어 모든 섹션이 같은 시점의 데이터가 되게 합니다.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var categories = await db.UserCategories.AsNoTracking().Where(c => c.OwnerId == ownerId)
            .OrderBy(c => c.Position).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        var methods = await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId)
            .OrderBy(m => m.Name).ThenBy(m => m.Id).ToListAsync(cancellationToken);
        var methodNames = methods.ToDictionary(m => m.Id, m => m.Name);
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.TagLinks).ThenInclude(link => link.Tag).AsSplitQuery()
            .Where(e => e.OwnerId == ownerId).OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var tagNames = (await db.Tags.AsNoTracking().Where(t => t.OwnerId == ownerId).Select(t => t.Name).ToListAsync(cancellationToken))
            .Order(StringComparer.Ordinal).ToList();
        var templates = await db.ExpenseTemplates.AsNoTracking().Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        var monthly = await db.MonthlyBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId)
            .OrderBy(b => b.Month).ToListAsync(cancellationToken);
        var categoryBudgets = await db.CategoryBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId)
            .OrderBy(b => b.Month).ThenBy(b => b.Category).ToListAsync(cancellationToken);
        var expenseRules = await db.RecurringExpenseRules.AsNoTracking().Where(r => r.OwnerId == ownerId)
            .OrderBy(r => r.DayOfMonth).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        var expenseOccurrences = (await db.RecurringExpenseOccurrences.AsNoTracking().Where(o => o.OwnerId == ownerId)
            .ToListAsync(cancellationToken)).ToLookup(o => o.RuleId, o => o.Month);
        var incomes = await db.Incomes.AsNoTracking().Where(i => i.OwnerId == ownerId)
            .OrderBy(i => i.Date).ThenBy(i => i.Id).ToListAsync(cancellationToken);
        var incomeRules = await db.RecurringIncomeRules.AsNoTracking().Where(r => r.OwnerId == ownerId)
            .OrderBy(r => r.DayOfMonth).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        var incomeOccurrences = (await db.RecurringIncomeOccurrences.AsNoTracking().Where(o => o.OwnerId == ownerId)
            .ToListAsync(cancellationToken)).ToLookup(o => o.RuleId, o => o.Month);
        var goals = await db.SavingsGoals.AsNoTracking().Where(g => g.OwnerId == ownerId)
            .OrderBy(g => g.CreatedDate).ThenBy(g => g.Id).ToListAsync(cancellationToken);
        var deposits = (await db.SavingsDeposits.AsNoTracking().Where(d => d.OwnerId == ownerId)
            .OrderBy(d => d.Date).ThenBy(d => d.Id).ToListAsync(cancellationToken)).ToLookup(d => d.GoalId);

        string? MethodName(int? id) => id is int value && methodNames.TryGetValue(value, out var name) ? name : null;
        List<string> Months(IEnumerable<DateTime> values) => values.OrderBy(v => v).Select(MonthText).ToList();

        // 카테고리 목록에 없는 이름이 기록에 남아 있어도(과거 데이터) 복원할 수 있게 목록에 포함합니다.
        var categoryItems = categories.Select(c => new BackupCategory(c.Name, c.Position, c.IsArchived)).ToList();
        var known = categoryItems.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var position = categories.Count == 0 ? 0 : categories.Max(c => c.Position) + 1;
        var referenced = expenses.Select(e => e.Category).Concat(templates.Select(t => t.Category))
            .Concat(categoryBudgets.Select(b => b.Category)).Concat(expenseRules.Select(r => r.Category))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var name in referenced)
            if (known.Add(name))
                categoryItems.Add(new BackupCategory(name, position++, false));

        var data = new BackupData(
            categoryItems,
            methods.Select(m => new BackupPaymentMethod(m.Name, m.Type)).ToList(),
            expenses.Select(e => new BackupExpense(DateOnly.FromDateTime(e.Date), e.Amount, e.Category, e.Memo, MethodName(e.PaymentMethodId), e.TagNames)).ToList(),
            templates.Select(t => new BackupTemplate(t.Name, t.Amount, t.Category, t.Memo, MethodName(t.PaymentMethodId))).ToList(),
            monthly.Select(b => new BackupMonthlyBudget(MonthText(b.Month), b.Amount)).ToList(),
            categoryBudgets.Select(b => new BackupCategoryBudget(MonthText(b.Month), b.Category, b.Amount)).ToList(),
            expenseRules.Select(r => new BackupRecurringExpense(MonthText(r.StartMonth), r.DayOfMonth, r.Amount, r.Category, r.Memo, r.IsActive, Months(expenseOccurrences[r.Id]))).ToList(),
            incomes.Select(i => new BackupIncome(DateOnly.FromDateTime(i.Date), i.Amount, i.Source, i.Memo)).ToList(),
            incomeRules.Select(r => new BackupRecurringIncome(MonthText(r.StartMonth), r.DayOfMonth, r.Amount, r.Source, r.Memo, r.IsActive, Months(incomeOccurrences[r.Id]))).ToList(),
            goals.Select(g => new BackupSavingsGoal(g.Name, g.TargetAmount, g.TargetDate is DateTime due ? DateOnly.FromDateTime(due) : null,
                DateOnly.FromDateTime(g.CreatedDate),
                deposits[g.Id].Select(d => new BackupSavingsDeposit(DateOnly.FromDateTime(d.Date), d.Amount, d.Memo)).ToList())).ToList(),
            tagNames);

        return new BackupFile(BackupFile.FormatName, BackupFile.CurrentVersion, now, BackupCounts.Of(data), data);
    }

    // 모든 작업을 한 트랜잭션에서 처리합니다. 하나라도 실패하면 아무것도 바뀌지 않으며,
    // dryRun이면 실제와 똑같이 실행한 뒤 되돌려 미리보기 결과가 실제 복원 결과와 같게 합니다.
    public async Task<BackupRestoreReport> RestoreAsync(string ownerId, BackupFile file, BackupRestoreMode mode, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(file);
        var issues = BackupValidator.Validate(file);
        if (issues.Count > 0)
            throw new ArgumentException(string.Join(" ", issues.Take(3)));
        var data = file.Data;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sections = new List<BackupSectionReport>();
        var warnings = new List<string>();
        var deleted = 0;

        if (mode == BackupRestoreMode.Replace)
            deleted = await DeleteAllAsync(db, ownerId, cancellationToken);
        else
            await CategoryService.EnsureAsync(db, ownerId, cancellationToken);

        // 카테고리: 이름이 같으면 기존 항목을 그대로 둡니다. 새 항목은 기존 목록 뒤에 백업의 순서대로 붙입니다.
        var existingCategories = await db.UserCategories.AsNoTracking().Where(c => c.OwnerId == ownerId).ToListAsync(cancellationToken);
        var categoryNames = existingCategories.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var nextPosition = existingCategories.Count == 0 ? 0 : existingCategories.Max(c => c.Position) + 1;
        var newCategories = new List<UserCategory>();
        foreach (var item in data.Categories.OrderBy(c => c.Position).ThenBy(c => c.Name, StringComparer.Ordinal))
            if (categoryNames.Add(item.Name))
                newCategories.Add(new UserCategory { OwnerId = ownerId, Name = item.Name, Position = nextPosition++, IsArchived = item.IsArchived });
        db.UserCategories.AddRange(newCategories);
        sections.Add(Report("카테고리", data.Categories.Count, newCategories.Count));

        // 결제수단: 이름이 같으면 기존 항목을 쓰고(유형이 다르면 알림), 없으면 만듭니다. 다른 항목이 ID로 참조하므로 먼저 저장합니다.
        var existingMethods = await db.PaymentMethods.AsNoTracking().Where(m => m.OwnerId == ownerId)
            .ToDictionaryAsync(m => m.Name, StringComparer.Ordinal, cancellationToken);
        var methodIds = existingMethods.ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal);
        var newMethods = new List<PaymentMethod>();
        foreach (var item in data.PaymentMethods)
        {
            if (existingMethods.TryGetValue(item.Name, out var current))
            {
                if (current.Type != item.Type)
                    warnings.Add($"결제수단 ‘{item.Name}’은(는) 이미 ‘{current.Type}’ 유형으로 있어 기존 항목을 사용합니다. (백업: {item.Type})");
            }
            else
            {
                newMethods.Add(new PaymentMethod { OwnerId = ownerId, Name = item.Name, Type = item.Type });
            }
        }
        db.PaymentMethods.AddRange(newMethods);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var method in newMethods)
            methodIds[method.Name] = method.Id;
        sections.Add(Report("결제수단", data.PaymentMethods.Count, newMethods.Count));
        int? MethodId(string? name) => name is null ? null : methodIds[name];

        // 태그: 이름이 같으면(대소문자 무시) 기존 태그를 쓰고 없으면 만듭니다. 지출이 ID로 연결하므로 먼저 저장합니다.
        var backupTagNames = (data.Tags ?? []).Concat(data.Expenses.SelectMany(e => e.Tags ?? [])).ToList();
        var existingTagKeys = (await db.Tags.AsNoTracking().Where(t => t.OwnerId == ownerId).Select(t => t.NormalizedName).ToListAsync(cancellationToken)).ToHashSet();
        var tagsByKey = await TagService.EnsureAsync(db, ownerId, backupTagNames, cancellationToken);
        var newTagCount = tagsByKey.Keys.Count(key => !existingTagKeys.Contains(key));
        await db.SaveChangesAsync(cancellationToken);
        if (data.Tags is not null) sections.Add(Report("태그", data.Tags.Count, newTagCount));

        // 지출: 같은 내용의 기존 기록 개수만큼만 건너뜁니다. 백업 안에서 내용이 같은 기록은 각각 별개의 기록으로 복원합니다.
        var existingExpenses = new Counter<(DateTime, long, string, string, int?)>(
            (await db.Expenses.AsNoTracking().Where(e => e.OwnerId == ownerId)
                .Select(e => new { e.Date, e.Amount, e.Category, e.Memo, e.PaymentMethodId }).ToListAsync(cancellationToken))
            .Select(e => (e.Date.Date, e.Amount, e.Category, e.Memo, e.PaymentMethodId)));
        var newExpenses = new List<ExpenseRecord>();
        foreach (var item in data.Expenses)
        {
            var methodId = MethodId(item.PaymentMethod);
            if (existingExpenses.TryConsume((Day(item.Date), item.Amount, item.Category, item.Memo, methodId))) continue;
            // 이미 있는 기록은 태그를 바꾸지 않습니다. 새로 만드는 기록에만 백업의 태그를 붙입니다.
            var record = new ExpenseRecord { OwnerId = ownerId, Date = Day(item.Date), Amount = item.Amount, Category = item.Category, Memo = item.Memo, PaymentMethodId = methodId };
            foreach (var tag in item.Tags ?? [])
                record.TagLinks.Add(new ExpenseTag { Expense = record, Tag = tagsByKey[TagNames.Key(tag)] });
            newExpenses.Add(record);
        }
        db.Expenses.AddRange(newExpenses);
        sections.Add(Report("지출", data.Expenses.Count, newExpenses.Count));

        var existingTemplates = new Counter<(string, long, string, string, int?)>(
            (await db.ExpenseTemplates.AsNoTracking().Where(t => t.OwnerId == ownerId)
                .Select(t => new { t.Name, t.Amount, t.Category, t.Memo, t.PaymentMethodId }).ToListAsync(cancellationToken))
            .Select(t => (t.Name, t.Amount, t.Category, t.Memo, t.PaymentMethodId)));
        var newTemplates = new List<ExpenseTemplate>();
        foreach (var item in data.Templates)
        {
            var methodId = MethodId(item.PaymentMethod);
            if (existingTemplates.TryConsume((item.Name, item.Amount, item.Category, item.Memo, methodId))) continue;
            newTemplates.Add(new ExpenseTemplate { OwnerId = ownerId, Name = item.Name, Amount = item.Amount, Category = item.Category, Memo = item.Memo, PaymentMethodId = methodId });
        }
        db.ExpenseTemplates.AddRange(newTemplates);
        sections.Add(Report("즐겨찾기 템플릿", data.Templates.Count, newTemplates.Count));

        // 예산: 같은 달(·카테고리)에 이미 예산이 있으면 기존 값을 유지합니다.
        var monthlyKeys = (await db.MonthlyBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId).Select(b => b.Month).ToListAsync(cancellationToken)).ToHashSet();
        var newMonthly = new List<MonthlyBudget>();
        foreach (var item in data.MonthlyBudgets)
            if (monthlyKeys.Add(Month(item.Month)))
                newMonthly.Add(new MonthlyBudget { OwnerId = ownerId, Month = Month(item.Month), Amount = item.Amount });
        db.MonthlyBudgets.AddRange(newMonthly);
        sections.Add(Report("월 예산", data.MonthlyBudgets.Count, newMonthly.Count));

        var categoryBudgetKeys = (await db.CategoryBudgets.AsNoTracking().Where(b => b.OwnerId == ownerId)
            .Select(b => new { b.Month, b.Category }).ToListAsync(cancellationToken)).Select(b => (b.Month, b.Category)).ToHashSet();
        var newCategoryBudgets = new List<CategoryBudget>();
        foreach (var item in data.CategoryBudgets)
            if (categoryBudgetKeys.Add((Month(item.Month), item.Category)))
                newCategoryBudgets.Add(new CategoryBudget { OwnerId = ownerId, Month = Month(item.Month), Category = item.Category, Amount = item.Amount });
        db.CategoryBudgets.AddRange(newCategoryBudgets);
        sections.Add(Report("카테고리 예산", data.CategoryBudgets.Count, newCategoryBudgets.Count));

        // 정기 지출: 내용이 같은 규칙이 이미 있으면 건너뜁니다. 새 규칙은 생성 이력도 함께 복원해 같은 달이 다시 생성되지 않게 합니다.
        var existingExpenseRules = new Counter<(int, long, string, string)>(
            (await db.RecurringExpenseRules.AsNoTracking().Where(r => r.OwnerId == ownerId)
                .Select(r => new { r.DayOfMonth, r.Amount, r.Category, r.Memo }).ToListAsync(cancellationToken))
            .Select(r => (r.DayOfMonth, r.Amount, r.Category, r.Memo)));
        var newExpenseRules = new List<(RecurringExpenseRule Rule, BackupRecurringExpense Source)>();
        foreach (var item in data.RecurringExpenses)
        {
            if (existingExpenseRules.TryConsume((item.DayOfMonth, item.Amount, item.Category, item.Memo))) continue;
            newExpenseRules.Add((new RecurringExpenseRule
            {
                OwnerId = ownerId, StartMonth = Month(item.StartMonth), DayOfMonth = item.DayOfMonth, Amount = item.Amount,
                Category = item.Category, Memo = item.Memo, IsActive = item.IsActive
            }, item));
        }
        db.RecurringExpenseRules.AddRange(newExpenseRules.Select(pair => pair.Rule));
        await db.SaveChangesAsync(cancellationToken);
        db.RecurringExpenseOccurrences.AddRange(newExpenseRules.SelectMany(pair => pair.Source.GeneratedMonths
            .Select(month => new RecurringExpenseOccurrence { RuleId = pair.Rule.Id, Month = Month(month), OwnerId = ownerId })));
        sections.Add(Report("정기 지출", data.RecurringExpenses.Count, newExpenseRules.Count));

        var existingIncomes = new Counter<(DateTime, long, string, string)>(
            (await db.Incomes.AsNoTracking().Where(i => i.OwnerId == ownerId)
                .Select(i => new { i.Date, i.Amount, i.Source, i.Memo }).ToListAsync(cancellationToken))
            .Select(i => (i.Date.Date, i.Amount, i.Source, i.Memo)));
        var newIncomes = new List<IncomeRecord>();
        foreach (var item in data.Incomes)
        {
            if (existingIncomes.TryConsume((Day(item.Date), item.Amount, item.Source, item.Memo))) continue;
            newIncomes.Add(new IncomeRecord { OwnerId = ownerId, Date = Day(item.Date), Amount = item.Amount, Source = item.Source, Memo = item.Memo });
        }
        db.Incomes.AddRange(newIncomes);
        sections.Add(Report("수입", data.Incomes.Count, newIncomes.Count));

        var existingIncomeRules = new Counter<(int, long, string, string)>(
            (await db.RecurringIncomeRules.AsNoTracking().Where(r => r.OwnerId == ownerId)
                .Select(r => new { r.DayOfMonth, r.Amount, r.Source, r.Memo }).ToListAsync(cancellationToken))
            .Select(r => (r.DayOfMonth, r.Amount, r.Source, r.Memo)));
        var newIncomeRules = new List<(RecurringIncomeRule Rule, BackupRecurringIncome Source)>();
        foreach (var item in data.RecurringIncomes)
        {
            if (existingIncomeRules.TryConsume((item.DayOfMonth, item.Amount, item.Source, item.Memo))) continue;
            newIncomeRules.Add((new RecurringIncomeRule
            {
                OwnerId = ownerId, StartMonth = Month(item.StartMonth), DayOfMonth = item.DayOfMonth, Amount = item.Amount,
                Source = item.Source, Memo = item.Memo, IsActive = item.IsActive
            }, item));
        }
        db.RecurringIncomeRules.AddRange(newIncomeRules.Select(pair => pair.Rule));
        await db.SaveChangesAsync(cancellationToken);
        db.RecurringIncomeOccurrences.AddRange(newIncomeRules.SelectMany(pair => pair.Source.GeneratedMonths
            .Select(month => new RecurringIncomeOccurrence { RuleId = pair.Rule.Id, Month = Month(month), OwnerId = ownerId })));
        sections.Add(Report("정기 수입", data.RecurringIncomes.Count, newIncomeRules.Count));

        // 목표 저축: 같은 이름의 목표가 이미 있으면 그 목표와 저축 내역은 건너뜁니다.
        var goalNames = (await db.SavingsGoals.AsNoTracking().Where(g => g.OwnerId == ownerId).Select(g => g.Name).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var existingGoalCount = goalNames.Count;
        var newGoals = new List<(SavingsGoal Goal, BackupSavingsGoal Source)>();
        foreach (var item in data.SavingsGoals)
        {
            if (!goalNames.Add(item.Name)) continue;
            newGoals.Add((new SavingsGoal
            {
                OwnerId = ownerId, Name = item.Name, TargetAmount = item.TargetAmount,
                TargetDate = item.TargetDate is DateOnly target ? Day(target) : null, CreatedDate = Day(item.CreatedDate)
            }, item));
        }
        if (existingGoalCount + newGoals.Count > SavingsGoalService.MaxGoals)
            throw new ArgumentException($"목표는 최대 {SavingsGoalService.MaxGoals}개까지 만들 수 있습니다. 현재 {existingGoalCount}개이고 백업에서 {newGoals.Count}개가 추가되어 초과합니다.");
        db.SavingsGoals.AddRange(newGoals.Select(pair => pair.Goal));
        await db.SaveChangesAsync(cancellationToken);
        var newDeposits = newGoals.SelectMany(pair => pair.Source.Deposits.Select(deposit => new SavingsDeposit
        {
            OwnerId = ownerId, GoalId = pair.Goal.Id, Date = Day(deposit.Date), Amount = deposit.Amount, Memo = deposit.Memo
        })).ToList();
        db.SavingsDeposits.AddRange(newDeposits);
        sections.Add(Report("목표 저축", data.SavingsGoals.Count, newGoals.Count));
        sections.Add(Report("저축 내역", file.Counts.SavingsDeposits, newDeposits.Count));

        await db.SaveChangesAsync(cancellationToken);
        if (dryRun)
            await transaction.RollbackAsync(cancellationToken);
        else
            await transaction.CommitAsync(cancellationToken);
        return new BackupRestoreReport(mode, dryRun, sections, warnings, deleted);
    }

    private static BackupSectionReport Report(string name, int total, int added) => new(name, total, added, total - added);

    // 덮어쓰기 전에 현재 데이터를 모두 지웁니다. 로그인 프로필은 유지합니다.
    private static async Task<int> DeleteAllAsync(ExpensesDbContext db, string ownerId, CancellationToken ct)
    {
        var deleted = 0;
        // 태그와 지출 연결을 먼저, 그다음 결제수단을 참조하는 지출·템플릿을 지웁니다.
        await db.ExpenseTags.Where(link => db.Tags.Any(tag => tag.Id == link.TagId && tag.OwnerId == ownerId)).ExecuteDeleteAsync(ct);
        deleted += await db.Tags.Where(t => t.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.Expenses.Where(e => e.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.ExpenseTemplates.Where(t => t.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.PaymentMethods.Where(m => m.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.MonthlyBudgets.Where(b => b.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.CategoryBudgets.Where(b => b.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        await db.RecurringExpenseOccurrences.Where(o => o.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.RecurringExpenseRules.Where(r => r.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.Incomes.Where(i => i.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        await db.RecurringIncomeOccurrences.Where(o => o.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.RecurringIncomeRules.Where(r => r.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.SavingsDeposits.Where(d => d.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.SavingsGoals.Where(g => g.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        deleted += await db.UserCategories.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync(ct);
        return deleted;
    }

    // 내용이 같은 항목의 개수를 세어, 기존 개수만큼만 "이미 있음"으로 처리합니다.
    private sealed class Counter<TKey>(IEnumerable<TKey> existing) where TKey : notnull
    {
        private readonly Dictionary<TKey, int> counts = existing.GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());

        public bool TryConsume(TKey key)
        {
            if (!counts.TryGetValue(key, out var remaining) || remaining == 0) return false;
            counts[key] = remaining - 1;
            return true;
        }
    }
}
