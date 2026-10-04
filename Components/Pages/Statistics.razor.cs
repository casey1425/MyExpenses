using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Statistics
{
    private string ownerId = string.Empty;
    private DateOnly today;
    private int year;
    private IReadOnlyList<int> years = [];
    private YearlyReport? report;
    private string? errorMessage;
    private bool isLoading;

    protected override async Task OnInitializedAsync()
    {
        today = DateOnly.FromDateTime(KoreanClock.Today);
        year = today.Year;
        years = [year];
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await ReloadAsync();
    }

    private async Task OnYearChangedAsync(ChangeEventArgs args)
    {
        if (!int.TryParse(args.Value?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
            !years.Contains(parsed))
        {
            errorMessage = "목록에 있는 해를 선택해 주세요.";
            return;
        }
        year = parsed;
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
            today = DateOnly.FromDateTime(KoreanClock.Today);
            // 정기 항목이 밀려 있으면 통계에 빠지므로 먼저 생성합니다. 생성 실패가 통계 조회를 막지는 않습니다.
            try
            {
                await RecurringExpenseService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
                await RecurringIncomeService.GenerateDueAsync(ownerId, today.ToDateTime(TimeOnly.MinValue));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "정기 항목을 생성하지 못했습니다.");
            }
            years = await StatisticsService.AvailableYearsAsync(ownerId, today);
            if (!years.Contains(year)) year = today.Year;
            report = await StatisticsService.LoadAsync(ownerId, year, today);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "연간 통계를 불러오지 못했습니다.");
            errorMessage = "연간 통계를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isLoading = false; }
    }

    private static string Period(DateOnly start, DateOnly end) => $"{start:yyyy.MM.dd} ~ {end:yyyy.MM.dd}";

    private static string RateText(double? rate) => rate is double value ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)}%" : "-";

    // 수입은 늘어나면 좋은 변화, 지출은 줄어들면 좋은 변화로 색을 칠합니다.
    private static string ChangeClass(long current, long previous, bool higherIsBetter)
    {
        if (current == previous) return "unchanged";
        return (current > previous) == higherIsBetter ? "good" : "bad";
    }

    private static string ChangeText(long current, long previous)
    {
        var difference = current - previous;
        if (difference == 0) return "전년도와 같음";
        var direction = difference > 0 ? "증가" : "감소";
        var rate = previous > 0 ? $" ({(Math.Abs(difference) * 100.0 / previous).ToString("0.#", CultureInfo.InvariantCulture)}%)" : "";
        return $"전년도보다 {Math.Abs(difference):N0}원 {direction}{rate}";
    }
}
