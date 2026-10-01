using System.Globalization;
using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Templates
{
    private string ownerId = "";
    private ExpenseTemplateInput input = new();
    private List<ExpenseTemplate> templates = [];
    private List<PaymentMethod> methods = [];
    private int? editingId;
    private int? deletingId;
    private bool busy;
    private bool loading = true;
    private string? error;
    private string? notice;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        await RefreshAsync();
    }

    private async Task CheckOwnerAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (ownerId == "" || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            throw new InvalidOperationException("로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.");
    }

    private async Task ReloadAsync()
    {
        await CheckOwnerAsync();
        var loaded = await TemplateService.ListAsync(ownerId);
        var loadedMethods = await MethodService.ListAsync(ownerId);
        await CheckOwnerAsync();
        templates = loaded;
        methods = loadedMethods;
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        loading = true;
        try { await ReloadAsync(); error = null; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; loading = false; }
    }

    private async Task SaveAsync()
    {
        if (busy) return;
        busy = true;
        notice = null;
        try
        {
            input.Validate();
            await CheckOwnerAsync();
            if (!await TemplateService.SaveAsync(ownerId, editingId, input))
                throw new InvalidOperationException("수정할 템플릿이 없습니다. 목록을 새로고침해 주세요.");
            CancelEdit();
            await ReloadAsync();
            error = null;
            notice = "템플릿을 저장했습니다. 지출 기록 화면에서 선택할 수 있어요.";
        }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private void Edit(ExpenseTemplate template)
    {
        if (busy) return;
        editingId = template.Id;
        deletingId = null;
        input = new() { Name = template.Name, Amount = template.Amount.ToString(CultureInfo.InvariantCulture), Category = template.Category, Memo = template.Memo, PaymentMethodId = template.PaymentMethodId };
        error = notice = null;
    }

    private void CancelEdit() { editingId = null; input = new(); }
    private void ConfirmDelete(int id) { if (!busy) { deletingId = id; notice = null; } }

    private async Task DeleteAsync(int id)
    {
        if (busy || deletingId != id) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await TemplateService.DeleteAsync(ownerId, id);
            if (editingId == id) CancelEdit();
            deletingId = null;
            await ReloadAsync();
            error = null;
            notice = "템플릿을 삭제했습니다. 기존 지출은 유지됩니다.";
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private void HandleError(Exception ex)
    {
        Logger.LogError(ex, "즐겨찾기 템플릿 처리에 실패했습니다.");
        error = "처리 결과를 확인하지 못했습니다. 페이지를 새로고침해 다시 확인해 주세요.";
    }
}
