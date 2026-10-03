using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Trends
{
    private string ownerId = string.Empty;
    private DateOnly today;
    private DateOnly selectedMonth;
    private string monthInput = string.Empty;
    private ExpenseTrendsReport? report;
    private string? errorMessage;
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        today = KoreanToday();
        selectedMonth = ExpenseTrends.MonthStart(today);
        monthInput = selectedMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await ReloadAsync();
    }

    private async Task OnMonthChangedAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString() ?? string.Empty;
        today = KoreanToday();
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateOnly.FromDateTime(parsed) < ExpenseTrends.MinimumMonth ||
            DateOnly.FromDateTime(parsed) > ExpenseTrends.MonthStart(today))
        {
            errorMessage = "이번 달까지의 유효한 월을 선택해 주세요.";
            monthInput = selectedMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return;
        }
        selectedMonth = DateOnly.FromDateTime(parsed);
        monthInput = value;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (isLoading) return;
        isLoading = true;
        errorMessage = null;
        report = null;
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (string.IsNullOrEmpty(ownerId) || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            {
                errorMessage = "로그인 상태가 바뀌었습니다. 페이지를 새로고침해 주세요.";
                return;
            }
            today = KoreanToday();
            await RecurringExpenseService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
            try
            {
                await RecurringIncomeService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
            }
            catch (Exception ex)
            {
                // 수입 생성 실패가 지출 추이 조회를 막지 않게 합니다.
                Logger.LogWarning(ex, "정기 수입을 생성하지 못했습니다.");
            }
            report = await TrendsService.LoadAsync(ownerId, selectedMonth, today);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "월별 지출 추이를 불러오지 못했습니다.");
            errorMessage = "지출 추이를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isLoading = false; }
    }

    private static DateOnly KoreanToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Seoul").DateTime);
    private static string Period(DateOnly start, DateOnly end) => $"{start:yyyy.MM.dd} ~ {end:yyyy.MM.dd}";
    private static string ChangeClass(ExpenseMonthComparison comparison) => comparison.Difference switch
    {
        > 0 => "increase",
        < 0 => "decrease",
        _ => "unchanged"
    };
    private static string DifferenceText(ExpenseMonthComparison comparison) => comparison.Difference switch
    {
        > 0 => $"{comparison.Difference:N0}원 증가",
        < 0 => $"{-comparison.Difference:N0}원 감소",
        _ => "0원 · 변동 없음"
    };
    private static string RateText(ExpenseMonthComparison comparison) => comparison.PercentageChange switch
    {
        > 0 => $"{comparison.PercentageChange:0.#}% 증가",
        < 0 => $"{-comparison.PercentageChange:0.#}% 감소",
        0 => "변동 없음",
        _ => comparison.CurrentAmount > 0 ? "전월 지출 없음" : "비교할 기록 없음"
    };
}
