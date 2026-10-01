using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class PaymentMethods
{
    private string ownerId = "";
    private string name = "";
    private string type = "체크카드";
    private static DateTime KoreanToday => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Seoul").Date;
    private DateTime month = new(KoreanToday.Year, KoreanToday.Month, 1);
    private string monthInput = KoreanToday.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private List<PaymentMethod> methods = [];
    private List<PaymentMethodTotal> totals = [];
    private int? editingId;
    private int? deletingId;
    private bool busy;
    private bool loading = true;
    private string? error;
    private string? notice;

    protected override async Task OnInitializedAsync()
    {
        ownerId = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        await RefreshAsync();
    }
    private async Task CheckOwnerAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (ownerId == "" || user.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId) throw new InvalidOperationException("계정이 변경되었습니다.");
    }
    private async Task ReloadAsync()
    {
        await CheckOwnerAsync();
        var summary = await MethodService.MonthlyTotalsAsync(ownerId, month);
        await CheckOwnerAsync();
        // 합계와 관리 목록을 같은 조회 결과에서 구성해 다른 탭의 수정·삭제에도 일관성을 유지합니다.
        totals = summary;
        methods = summary.Where(t => t.Id.HasValue).Select(t => new PaymentMethod { Id = t.Id!.Value, Name = t.Name, Type = t.Type }).ToList();
    }
    private async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try { await ReloadAsync(); error = null; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; loading = false; }
    }
    private async Task MonthChangedAsync(ChangeEventArgs args)
    {
        if (busy) return;
        if (!DateTime.TryParseExact(args.Value?.ToString(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected))
        { error = "조회할 월을 선택해 주세요."; return; }
        var previous = month;
        month = selected;
        busy = true;
        try { await ReloadAsync(); monthInput = selected.ToString("yyyy-MM", CultureInfo.InvariantCulture); error = null; }
        catch (Exception ex) { month = previous; HandleError(ex); }
        finally { busy = false; }
    }
    private async Task SaveAsync()
    {
        if (busy) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            if (!await MethodService.SaveAsync(ownerId, editingId, name, type)) throw new InvalidOperationException("결제수단이 없습니다.");
            CancelEdit();
            await ReloadAsync();
            error = null;
            notice = "결제수단을 저장했습니다. 지출 입력 화면에서 선택할 수 있어요.";
        }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }
    private void Edit(PaymentMethod method) { if (!busy) { editingId = method.Id; deletingId = null; name = method.Name; type = method.Type; notice = error = null; } }
    private void CancelEdit() { editingId = null; name = ""; type = "체크카드"; }
    private async Task DeleteAsync(int id)
    {
        if (busy || deletingId != id) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await MethodService.DeleteAsync(ownerId, id);
            if (editingId == id) CancelEdit();
            deletingId = null;
            await ReloadAsync();
            error = null;
            notice = "삭제했습니다. 연결된 기록은 미지정으로 유지됩니다.";
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }
    private void HandleError(Exception ex) { Logger.LogError(ex, "결제수단 처리 실패"); error = "처리 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요."; }
}
