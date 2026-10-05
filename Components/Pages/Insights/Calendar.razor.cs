using System.Globalization;
using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Calendar
{
    private string ownerId = string.Empty;
    private DateOnly today;
    private DateOnly month;
    private DateOnly? selected;
    private CalendarData? data;
    private string? errorMessage;
    private bool isLoading;

    private IReadOnlyList<ExpenseRecord> dayExpenses => selected is DateOnly date && data is not null
        ? data.Expenses.Where(e => DateOnly.FromDateTime(e.Date) == date).ToList() : [];
    private IReadOnlyList<IncomeRecord> dayIncomes => selected is DateOnly date && data is not null
        ? data.Incomes.Where(i => DateOnly.FromDateTime(i.Date) == date).ToList() : [];

    protected override async Task OnInitializedAsync()
    {
        today = DateOnly.FromDateTime(KoreanClock.Today);
        month = new DateOnly(today.Year, today.Month, 1);
        selected = today;
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await ReloadAsync();
    }

    private Task ShiftAsync(int delta)
    {
        DateOnly target;
        try { target = month.AddMonths(delta); }
        catch (ArgumentOutOfRangeException) { return Task.CompletedTask; }
        if (target < CalendarMonth.MinimumMonth || target > CalendarMonth.MaximumMonth) return Task.CompletedTask;
        return ShowAsync(target);
    }

    private Task GoTodayAsync()
    {
        today = DateOnly.FromDateTime(KoreanClock.Today);
        return ShowAsync(new DateOnly(today.Year, today.Month, 1), today);
    }

    private async Task ShowAsync(DateOnly target, DateOnly? select = null)
    {
        if (isLoading) return;
        month = target;
        // 이번 달로 돌아오면 오늘을, 다른 달로 가면 선택을 비워 이전 달의 날짜가 남지 않게 합니다.
        selected = select ?? (target == new DateOnly(today.Year, today.Month, 1) ? today : null);
        await ReloadAsync();
    }

    private void Select(DateOnly date) => selected = selected == date ? null : date;

    private async Task ReloadAsync()
    {
        if (isLoading) return;
        isLoading = true;
        errorMessage = null;
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (string.IsNullOrEmpty(ownerId) || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            {
                data = null;
                errorMessage = "로그인 상태가 바뀌었습니다. 페이지를 새로고침해 주세요.";
                return;
            }
            today = DateOnly.FromDateTime(KoreanClock.Today);
            // 밀린 정기 항목이 빠지지 않게 먼저 생성합니다. 실패해도 달력 조회는 계속합니다.
            try
            {
                await RecurringExpenseService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
                await RecurringIncomeService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "정기 항목을 생성하지 못했습니다.");
            }
            data = await CalendarService.LoadAsync(ownerId, month, today);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "달력 데이터를 불러오지 못했습니다.");
            data = null;
            errorMessage = "달력을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isLoading = false; }
    }

    private static string DayLabel(CalendarDay day)
    {
        var parts = new List<string> { $"{day.Date.Month}월 {day.Date.Day}일" };
        parts.Add(day.Expense > 0 ? $"지출 {day.Expense:N0}원 {day.ExpenseCount}건" : "지출 없음");
        if (day.Income > 0) parts.Add($"수입 {day.Income:N0}원 {day.IncomeCount}건");
        if (day.IsToday) parts.Add("오늘");
        return string.Join(", ", parts);
    }
}
