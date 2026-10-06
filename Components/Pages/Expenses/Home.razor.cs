using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Components.Expenses;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Home
{
    // 달력 화면 등에서 /?date=2026-10-05 로 들어오면 그 날짜로 지출을 입력합니다.
    [Parameter, SupplyParameterFromQuery(Name = "date")] public string? DateParameter { get; set; }
    // 태그 관리 화면 등에서 /?tag=3 으로 들어오면 그 태그의 지출만 보여 줍니다.
    [Parameter, SupplyParameterFromQuery(Name = "tag")] public string? TagParameter { get; set; }

    private List<UserCategory> categoryItems = [];
    private IReadOnlyList<string> categories = ExpenseCategories.All;
    private IReadOnlyList<string> allCategories => categoryItems.Count == 0 ? categories : categoryItems.Select(c => c.Name).ToList();
    private IEnumerable<string> EditCategories => categories.Concat(new[] { editCategory }).Distinct();
    private string CategoryLabel(string value) => categoryItems.Any(c => c.Name == value && c.IsArchived) ? value + " (보관)" : value;
    private string ownerId = string.Empty;
    private List<ExpenseRecord> expenses = [];
    private IReadOnlyList<CategoryStatistic> categoryStatistics = [];
    private DateTime expenseDate = KoreanClock.Today;
    private long amount;
    private string category = "식비";
    private string memo = string.Empty;
    private List<PaymentMethod> paymentMethods = [];
    private int? paymentMethodId;
    private int? editPaymentMethodId;
    private bool paymentBusy;
    private string? paymentError;
    private List<MemoSuggestion> memoSuggestions = [];
    private string tagsText = string.Empty;
    private string editTagsText = string.Empty;
    private List<TagUsage> tagUsage = [];
    private string? autofillNotice;
    private ExpenseInput? lastDeleted;
    private bool undoBusy;
    private ExpenseForm? entryForm;
    private List<ExpenseTemplate> templates = [];
    private string selectedTemplateId = "";
    private bool templateBusy;
    private string? templateError;
    private string? templateNotice;
    private string? errorMessage;
    private string? saveNotice;
    private string? deleteError;
    private string? historyNotice;
    private string? filterError;
    private ExpenseSearchInput searchInput = new();
    private bool isSearching;
    private ExpenseFilter activeFilter = new();
    private DateTime budgetMonth = new(KoreanClock.Today.Year, KoreanClock.Today.Month, 1);
    private string budgetMonthInput = KoreanClock.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private long budgetInput;
    private long? budgetAmount;
    private long budgetSpent;
    private Dictionary<string, long> categoryBudgetSpent = [];
    private Dictionary<string, long> categoryBudgetAmounts = [];
    private readonly Dictionary<string, long> categoryBudgetInputs = ExpenseCategories.All.ToDictionary(category => category, _ => 0L);
    private string? categoryBudgetError;
    private string? categoryBudgetNotice;
    private string? savingCategoryBudget;
    private string? budgetError;
    private string? budgetNotice;
    private bool isSavingBudget;
    private bool showDeleteAllConfirmation;
    private bool isDeletingAll;
    private int? editingId;
    private DateTime editDate = KoreanClock.Today;
    private long editAmount;
    private string editCategory = "식비";
    private string editMemo = string.Empty;
    private string? editError;
    private bool isSavingEdit;
    private string UndoLabel => lastDeleted is null ? string.Empty : $"{lastDeleted.Category} {lastDeleted.Amount:N0}원";
    // 월 이동만으로는 검색 조건을 펼쳐 두지 않도록, 월을 뺀 나머지 조건이 있을 때만 "적용 중"으로 봅니다.
    private bool HasDetailedFilter => (activeFilter with { Month = null }).IsActive;
    private string MonthNavLabel => activeFilter.Month is DateTime month ? month.ToString("yyyy년 M월") : "전체 기간";
    // 자주 쓰는 태그를 입력 칸 아래에 바로 고를 수 있게 보여 줍니다.
    private IReadOnlyList<TagUsage> TagChoices => tagUsage.OrderByDescending(tag => tag.ExpenseCount)
        .ThenBy(tag => tag.Name, StringComparer.Ordinal).Take(8).ToList();
    private decimal FilteredTotal => expenses.Sum(item => (decimal)item.Amount);
    private decimal MonthlyTotal => expenses
        .Where(item => item.Date.Year == KoreanClock.Today.Year && item.Date.Month == KoreanClock.Today.Month)
        .Sum(item => (decimal)item.Amount);
    private decimal StatisticsTotal => categoryStatistics.Sum(statistic => statistic.Amount);
    private long? CategoryBudgetAmount(string selectedCategory) =>
        categoryBudgetAmounts.TryGetValue(selectedCategory, out var value) ? value : null;
    private string StatisticsPeriodLabel => activeFilter.IsActive
        ? FilterDescription
        : $"{KoreanClock.Today:yyyy년 M월} 기준";
    private string FilterDescription
    {
        get
        {
            var parts = new List<string>();
            if (activeFilter.Month is DateTime month) parts.Add(month.ToString("yyyy년 M월"));
            if (activeFilter.StartDate.HasValue || activeFilter.EndDate.HasValue)
                parts.Add($"{activeFilter.StartDate?.ToString("yyyy-MM-dd") ?? "시작 제한 없음"} ~ {activeFilter.EndDate?.ToString("yyyy-MM-dd") ?? "종료 제한 없음"}");
            if (parts.Count == 0) parts.Add("전체 기간");
            if (!string.IsNullOrEmpty(activeFilter.Category)) parts.Add(activeFilter.Category);
            if (!string.IsNullOrEmpty(activeFilter.Search)) parts.Add($"메모: {activeFilter.Search}");
            if (activeFilter.MinAmount.HasValue || activeFilter.MaxAmount.HasValue)
                parts.Add($"{activeFilter.MinAmount?.ToString("N0") ?? "0"}원 ~ {activeFilter.MaxAmount?.ToString("N0") ?? "제한 없음"}");
            if (activeFilter.UnspecifiedPayment) parts.Add("결제수단: 미지정");
            if (activeFilter.TagId is int tagId) parts.Add($"태그: {tagUsage.FirstOrDefault(t => t.Id == tagId)?.Name ?? "삭제된 태그"}");
            if (activeFilter.PaymentMethodId is int methodId) parts.Add($"결제수단: {paymentMethods.FirstOrDefault(m => m.Id == methodId)?.Name ?? "삭제된 수단"}");
            return string.Join(" · ", parts);
        }
    }
    private string FilteredExportUrl
    {
        get
        {
            var url = "/export/expenses.csv?scope=filtered";
            if (activeFilter.Month is DateTime month)
                url += $"&month={month.ToString("yyyy-MM", CultureInfo.InvariantCulture)}";
            if (activeFilter.Category is string category)
                url += $"&category={Uri.EscapeDataString(category)}";
            if (!string.IsNullOrEmpty(activeFilter.Search)) url += $"&search={Uri.EscapeDataString(activeFilter.Search)}";
            if (activeFilter.StartDate is DateTime start) url += $"&start={start:yyyy-MM-dd}";
            if (activeFilter.EndDate is DateTime end) url += $"&end={end:yyyy-MM-dd}";
            if (activeFilter.MinAmount is long min) url += $"&min={min.ToString(CultureInfo.InvariantCulture)}";
            if (activeFilter.MaxAmount is long max) url += $"&max={max.ToString(CultureInfo.InvariantCulture)}";
            url += $"&sort={activeFilter.Sort}";
            if (activeFilter.UnspecifiedPayment) url += "&payment=none";
            if (activeFilter.PaymentMethodId is int methodId) url += $"&payment={methodId}";
            if (activeFilter.TagId is int tagId) url += $"&tag={tagId}";
            return url;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrEmpty(ownerId))
        {
            Navigation.NavigateTo("/account/login", forceLoad: true);
            return;
        }

        if (DateTime.TryParseExact(DateParameter, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedDate) &&
            requestedDate.Year >= 2)
            expenseDate = requestedDate;

        if (await UserDataProvisioner.EnsureUserAsync(ownerId))
        {
            Navigation.NavigateTo("/welcome");
            return;
        }

        if (int.TryParse(TagParameter, NumberStyles.None, CultureInfo.InvariantCulture, out var requestedTag) && requestedTag > 0)
        {
            searchInput.Tag = requestedTag.ToString(CultureInfo.InvariantCulture);
            if (searchInput.TryCreate(out var initialFilter, out _)) activeFilter = initialFilter;
        }

        try
        {
            await LoadCategoriesAsync();
            await RecurringExpenseService.GenerateDueAsync(ownerId, KoreanClock.Today);
            await GenerateRecurringIncomeAsync();
            await LoadExpensesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "지출 내역을 불러오지 못했습니다.");
            errorMessage = "저장된 지출 내역을 불러오지 못했습니다. 앱을 다시 실행해 주세요.";
        }

        await RefreshBudgetAsync();
        await RefreshPaymentMethodsAsync();
        await RefreshTemplatesAsync();
        await LoadSuggestionsAsync();
        await LoadTagsAsync();
    }

    private async Task LoadCategoriesAsync()
    {
        await CheckOwnerAsync();
        var loaded = await CategoryService.ListAsync(ownerId);
        await CheckOwnerAsync();
        categoryItems = loaded;
        categories = loaded.Where(c => !c.IsArchived).Select(c => c.Name).ToList();
        if (!categories.Contains(category)) category = categories.FirstOrDefault() ?? "";
        foreach (var name in allCategories) categoryBudgetInputs.TryAdd(name, 0);
    }

    private async Task CheckOwnerAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (ownerId == "" || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            throw new InvalidOperationException("로그인 계정이 바뀌었습니다.");
    }

    // 정기 수입 생성이 실패해도 지출 화면은 계속 사용할 수 있게 별도로 처리합니다.
    private async Task GenerateRecurringIncomeAsync()
    {
        try
        {
            await RecurringIncomeService.GenerateDueAsync(ownerId, KoreanClock.Today);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "정기 수입을 생성하지 못했습니다.");
        }
    }
}
