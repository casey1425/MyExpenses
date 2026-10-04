using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyExpenses.Data;

// 내 데이터 전체 백업 파일(JSON)의 구조입니다. 계정 정보·내부 ID는 포함하지 않고,
// 결제수단은 이름으로 참조하므로 다른 계정으로도 복원할 수 있습니다.
public sealed record BackupFile(string Format, int Version, DateTimeOffset ExportedAt, BackupCounts Counts, BackupData Data)
{
    public const string FormatName = "MyExpensesBackup";
    public const int CurrentVersion = 1;
}

public sealed record BackupCounts(int Categories, int PaymentMethods, int Expenses, int Templates, int MonthlyBudgets,
    int CategoryBudgets, int RecurringExpenses, int Incomes, int RecurringIncomes, int SavingsGoals, int SavingsDeposits)
{
    [JsonIgnore]
    public int Total => Categories + PaymentMethods + Expenses + Templates + MonthlyBudgets + CategoryBudgets +
                        RecurringExpenses + Incomes + RecurringIncomes + SavingsGoals + SavingsDeposits;

    public static BackupCounts Of(BackupData data) => new(data.Categories.Count, data.PaymentMethods.Count, data.Expenses.Count,
        data.Templates.Count, data.MonthlyBudgets.Count, data.CategoryBudgets.Count, data.RecurringExpenses.Count,
        data.Incomes.Count, data.RecurringIncomes.Count, data.SavingsGoals.Count, data.SavingsGoals.Sum(goal => goal.Deposits.Count));
}

public sealed record BackupData(
    IReadOnlyList<BackupCategory> Categories,
    IReadOnlyList<BackupPaymentMethod> PaymentMethods,
    IReadOnlyList<BackupExpense> Expenses,
    IReadOnlyList<BackupTemplate> Templates,
    IReadOnlyList<BackupMonthlyBudget> MonthlyBudgets,
    IReadOnlyList<BackupCategoryBudget> CategoryBudgets,
    IReadOnlyList<BackupRecurringExpense> RecurringExpenses,
    IReadOnlyList<BackupIncome> Incomes,
    IReadOnlyList<BackupRecurringIncome> RecurringIncomes,
    IReadOnlyList<BackupSavingsGoal> SavingsGoals);

public sealed record BackupCategory(string Name, int Position, bool IsArchived);
public sealed record BackupPaymentMethod(string Name, string Type);
public sealed record BackupExpense(DateOnly Date, long Amount, string Category, string Memo, string? PaymentMethod);
public sealed record BackupTemplate(string Name, long Amount, string Category, string Memo, string? PaymentMethod);
public sealed record BackupMonthlyBudget(string Month, long Amount);
public sealed record BackupCategoryBudget(string Month, string Category, long Amount);

// GeneratedMonths는 이미 자동 생성이 끝난 달입니다. 복원 후 같은 달이 다시 생성되어 기록이 중복되는 것을 막습니다.
public sealed record BackupRecurringExpense(string StartMonth, int DayOfMonth, long Amount, string Category, string Memo,
    bool IsActive, IReadOnlyList<string> GeneratedMonths);

public sealed record BackupIncome(DateOnly Date, long Amount, string Source, string Memo);

public sealed record BackupRecurringIncome(string StartMonth, int DayOfMonth, long Amount, string Source, string Memo,
    bool IsActive, IReadOnlyList<string> GeneratedMonths);

public sealed record BackupSavingsGoal(string Name, long TargetAmount, DateOnly? TargetDate, DateOnly CreatedDate,
    IReadOnlyList<BackupSavingsDeposit> Deposits);

public sealed record BackupSavingsDeposit(DateOnly Date, long Amount, string Memo);

public sealed record BackupParseResult(BackupFile? File, IReadOnlyList<string> Issues);

public static class BackupLimits
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxRows = 100_000;
}

public static class BackupSerializer
{
    // 한글이 \uXXXX로 바뀌지 않게 해 파일 크기와 가독성을 지킵니다. 응답은 항상 application/json 첨부 파일로만 내려줍니다.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 신뢰할 수 없는 파일이므로 알 수 없는 항목·중복 항목·누락·null을 모두 오류로 처리합니다.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static byte[] Serialize(BackupFile file) => JsonSerializer.SerializeToUtf8Bytes(file, WriteOptions);

    public static BackupParseResult Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            return new(null, ["빈 파일입니다."]);
        if (bytes.Length > BackupLimits.MaxFileBytes)
            return new(null, [$"파일이 너무 큽니다. 최대 {BackupLimits.MaxFileBytes / 1024 / 1024}MB까지 복원할 수 있습니다."]);

        BackupFile? file;
        try
        {
            file = JsonSerializer.Deserialize<BackupFile>(bytes, ReadOptions);
        }
        catch (JsonException ex)
        {
            return new(null, [$"MyExpenses 백업 파일 형식이 올바르지 않습니다. (위치: {ex.Path ?? "$"})"]);
        }
        if (file is null)
            return new(null, ["MyExpenses 백업 파일 형식이 올바르지 않습니다."]);

        var issues = BackupValidator.Validate(file);
        return issues.Count > 0 ? new(null, issues) : new(file, []);
    }
}

public static class BackupValidator
{
    public static readonly DateOnly MinDate = new(1900, 1, 1);
    public static readonly DateOnly MaxDate = new(2200, 12, 31);
    private static readonly DateOnly GoalMinDate = new(2000, 1, 1);
    private static readonly DateOnly GoalMaxDate = new(2100, 12, 31);
    private const int MaxIssues = 20;

    // 서비스가 새로 입력받을 때 적용하는 규칙과 같게 맞춰, 앱에서 만든 데이터는 항상 다시 복원할 수 있습니다.
    public static List<string> Validate(BackupFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Format != BackupFile.FormatName)
            return ["MyExpenses 백업 파일이 아닙니다."];
        if (file.Version != BackupFile.CurrentVersion)
            return [file.Version > BackupFile.CurrentVersion
                ? "더 새로운 버전의 백업 파일입니다. 앱을 최신 버전으로 업데이트한 뒤 다시 시도해 주세요."
                : "지원하지 않는 백업 파일 버전입니다."];
        if (file.ExportedAt == default)
            return ["내보낸 시각이 없는 백업 파일입니다."];

        var data = file.Data;
        var actual = BackupCounts.Of(data);
        if (actual != file.Counts)
            return ["파일이 손상되었거나 일부가 잘렸을 수 있습니다. 항목 수가 파일에 기록된 값과 다릅니다."];
        if (actual.Total > BackupLimits.MaxRows)
            return [$"항목이 너무 많습니다. 최대 {BackupLimits.MaxRows:N0}건까지 복원할 수 있습니다."];

        var report = new IssueCollector();

        var categories = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < data.Categories.Count; i++)
        {
            var item = data.Categories[i];
            if (!ValidName(item.Name, 30) || item.Name.Any(char.IsControl)) report.Add("카테고리", i, "이름은 제어문자 없이 1~30자여야 합니다.");
            else if (!categories.Add(item.Name)) report.Add("카테고리", i, "같은 이름이 중복되었습니다.");
            if (item.Position is < 0 or > 100_000) report.Add("카테고리", i, "순서 값이 올바르지 않습니다.");
        }

        var methods = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < data.PaymentMethods.Count; i++)
        {
            var item = data.PaymentMethods[i];
            if (!ValidName(item.Name, 50)) report.Add("결제수단", i, "이름은 1~50자여야 합니다.");
            else if (!methods.Add(item.Name)) report.Add("결제수단", i, "같은 이름이 중복되었습니다.");
            if (!PaymentMethod.Types.Contains(item.Type)) report.Add("결제수단", i, "결제 유형이 올바르지 않습니다.");
        }

        // 지출·템플릿·예산·규칙이 공통으로 쓰는 항목(금액·카테고리·결제수단·메모) 검사입니다.
        void CheckFields(string section, int index, long amount, string? category, string? method, string? memo)
        {
            if (amount < 1) report.Add(section, index, "금액은 1원 이상이어야 합니다.");
            if (category is not null && !categories.Contains(category))
                report.Add(section, index, $"백업의 카테고리 목록에 없는 카테고리입니다: ‘{Shorten(category)}’");
            if (method is not null && !methods.Contains(method))
                report.Add(section, index, $"백업의 결제수단 목록에 없는 결제수단입니다: ‘{Shorten(method)}’");
            if (memo is not null && memo.Length > 100)
                report.Add(section, index, "메모는 100자 이하여야 합니다.");
        }

        void CheckDate(string section, int index, DateOnly date)
        {
            if (date < MinDate || date > MaxDate) report.Add(section, index, "날짜가 올바르지 않습니다.");
        }

        for (var i = 0; i < data.Expenses.Count; i++)
        {
            var item = data.Expenses[i];
            CheckDate("지출", i, item.Date);
            CheckFields("지출", i, item.Amount, item.Category, item.PaymentMethod, item.Memo);
        }

        for (var i = 0; i < data.Templates.Count; i++)
        {
            var item = data.Templates[i];
            if (!ValidName(item.Name, 50)) report.Add("템플릿", i, "이름은 1~50자여야 합니다.");
            CheckFields("템플릿", i, item.Amount, item.Category, item.PaymentMethod, item.Memo);
        }

        var monthlyKeys = new HashSet<DateTime>();
        for (var i = 0; i < data.MonthlyBudgets.Count; i++)
        {
            var item = data.MonthlyBudgets[i];
            if (!TryParseMonth(item.Month, out var month)) report.Add("월 예산", i, "월은 yyyy-MM 형식이어야 합니다.");
            else if (!monthlyKeys.Add(month)) report.Add("월 예산", i, "같은 달의 예산이 중복되었습니다.");
            CheckFields("월 예산", i, item.Amount, null, null, null);
        }

        var categoryBudgetKeys = new HashSet<(DateTime, string)>();
        for (var i = 0; i < data.CategoryBudgets.Count; i++)
        {
            var item = data.CategoryBudgets[i];
            if (!TryParseMonth(item.Month, out var month)) report.Add("카테고리 예산", i, "월은 yyyy-MM 형식이어야 합니다.");
            else if (!categoryBudgetKeys.Add((month, item.Category))) report.Add("카테고리 예산", i, "같은 달·카테고리의 예산이 중복되었습니다.");
            CheckFields("카테고리 예산", i, item.Amount, item.Category, null, null);
        }

        for (var i = 0; i < data.RecurringExpenses.Count; i++)
        {
            var item = data.RecurringExpenses[i];
            CheckRule(report, "정기 지출", i, item.StartMonth, item.DayOfMonth, item.GeneratedMonths);
            CheckFields("정기 지출", i, item.Amount, item.Category, null, item.Memo);
        }

        for (var i = 0; i < data.Incomes.Count; i++)
        {
            var item = data.Incomes[i];
            CheckDate("수입", i, item.Date);
            CheckFields("수입", i, item.Amount, null, null, item.Memo);
            if (!IncomeRecord.Sources.Contains(item.Source)) report.Add("수입", i, "분류가 올바르지 않습니다.");
        }

        for (var i = 0; i < data.RecurringIncomes.Count; i++)
        {
            var item = data.RecurringIncomes[i];
            CheckRule(report, "정기 수입", i, item.StartMonth, item.DayOfMonth, item.GeneratedMonths);
            CheckFields("정기 수입", i, item.Amount, null, null, item.Memo);
            if (!IncomeRecord.Sources.Contains(item.Source)) report.Add("정기 수입", i, "분류가 올바르지 않습니다.");
        }

        if (data.SavingsGoals.Count > Services.SavingsGoalService.MaxGoals)
            report.Add("목표 저축", 0, $"목표는 최대 {Services.SavingsGoalService.MaxGoals}개까지 복원할 수 있습니다.");
        var goalNames = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < data.SavingsGoals.Count; i++)
        {
            var goal = data.SavingsGoals[i];
            if (!ValidName(goal.Name, 50)) report.Add("목표 저축", i, "이름은 1~50자여야 합니다.");
            else if (!goalNames.Add(goal.Name)) report.Add("목표 저축", i, "같은 이름의 목표가 중복되었습니다.");
            if (goal.TargetAmount is < 1 or > Services.SavingsGoalService.MaxAmount) report.Add("목표 저축", i, "목표 금액이 올바르지 않습니다.");
            if (goal.TargetDate is DateOnly target && (target < GoalMinDate || target > GoalMaxDate)) report.Add("목표 저축", i, "목표일이 올바르지 않습니다.");
            CheckDate("목표 저축", i, goal.CreatedDate);
            for (var d = 0; d < goal.Deposits.Count; d++)
            {
                var deposit = goal.Deposits[d];
                CheckDate("저축 내역", d, deposit.Date);
                if (deposit.Amount is < 1 or > Services.SavingsGoalService.MaxAmount) report.Add("저축 내역", d, $"‘{Shorten(goal.Name)}’의 금액이 올바르지 않습니다.");
                if (deposit.Memo.Length > 100) report.Add("저축 내역", d, $"‘{Shorten(goal.Name)}’의 메모는 100자 이하여야 합니다.");
            }
        }

        return report.ToList();
    }

    public static bool TryParseMonth(string value, out DateTime month)
    {
        month = default;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        if (parsed.Year < MinDate.Year || parsed.Year > MaxDate.Year) return false;
        month = new DateTime(parsed.Year, parsed.Month, 1);
        return true;
    }

    private static void CheckRule(IssueCollector report, string section, int index, string startMonth, int day, IReadOnlyList<string> generated)
    {
        if (!TryParseMonth(startMonth, out _)) report.Add(section, index, "시작 월은 yyyy-MM 형식이어야 합니다.");
        if (day is < 1 or > 31) report.Add(section, index, "지정일은 1~31일이어야 합니다.");
        var seen = new HashSet<DateTime>();
        foreach (var value in generated)
        {
            if (!TryParseMonth(value, out var month)) { report.Add(section, index, "생성 이력의 월 형식이 올바르지 않습니다."); break; }
            if (!seen.Add(month)) { report.Add(section, index, "생성 이력에 같은 달이 중복되었습니다."); break; }
        }
    }

    private static bool ValidName(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    private static string Shorten(string value) => value.Length <= 20 ? value : value[..20] + "…";

    private sealed class IssueCollector
    {
        private readonly List<string> issues = [];
        private int suppressed;

        public void Add(string section, int zeroBasedIndex, string message)
        {
            if (issues.Count < MaxIssues) issues.Add($"{section} {zeroBasedIndex + 1}번째: {message}");
            else suppressed++;
        }

        public List<string> ToList()
        {
            var result = new List<string>(issues);
            if (suppressed > 0) result.Add($"그 밖에 {suppressed}건의 오류가 있습니다.");
            return result;
        }
    }
}
