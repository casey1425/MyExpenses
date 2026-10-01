using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Recurring
{
    private IReadOnlyList<string> categories = [];
    private string ownerId = string.Empty;
    private List<RecurringExpenseRule> rules = [];
    private int dayOfMonth = DateTime.Today.Day;
    private long amount;
    private string category = "생활";
    private string memo = string.Empty;
    private int? editingId;
    private int editDay;
    private long editAmount;
    private string editCategory = "생활";
    private string editMemo = string.Empty;
    private int? deletingId;
    private bool isBusy;
    private string? errorMessage;
    private string? notice;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrEmpty(ownerId))
            return;

        try
        {
            await RecurringExpenseService.GenerateDueAsync(ownerId, DateTime.Today);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "정기 지출을 불러오지 못했습니다.");
            errorMessage = "정기 지출을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private async Task ReloadAsync()
    {
        var loaded = await CategoryService.ListAsync(ownerId);
        categories = loaded.Where(c => !c.IsArchived).Select(c => c.Name).ToList();
        if (!categories.Contains(category)) category = categories.FirstOrDefault() ?? "";
        rules = await RecurringExpenseService.ListAsync(ownerId);
    }

    private async Task<bool> CheckOwnerAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (string.IsNullOrEmpty(ownerId) || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
        {
            errorMessage = "로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.";
            return false;
        }
        return true;
    }

    private bool Validate(int day, long value, string selectedCategory, string text)
    {
        if (day is >= 1 and <= 31 && value > 0 && (categories.Contains(selectedCategory) || (editingId.HasValue && rules.Any(r => r.Id == editingId && r.Category == selectedCategory))) && text.Trim().Length <= 100)
            return true;
        errorMessage = "지정일(1~31일), 1원 이상의 금액, 카테고리와 100자 이하 메모를 확인해 주세요.";
        return false;
    }

    private async Task CreateAsync()
    {
        if (isBusy || !Validate(dayOfMonth, amount, category, memo) || !await CheckOwnerAsync())
            return;
        isBusy = true;
        try
        {
            await RecurringExpenseService.CreateAsync(ownerId, dayOfMonth, amount, category, memo);
            var generated = await RecurringExpenseService.GenerateDueAsync(ownerId, DateTime.Today);
            await ReloadAsync();
            amount = 0;
            memo = string.Empty;
            errorMessage = null;
            notice = generated > 0 ? "규칙을 등록하고 이번 달 지출을 기록했습니다." : "정기 지출 규칙을 등록했습니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "정기 지출 등록에 실패했습니다.");
            errorMessage = "등록 결과를 확인하지 못했습니다. 목록을 새로고침해 주세요.";
        }
        finally { isBusy = false; }
    }

    private void StartEdit(RecurringExpenseRule rule)
    {
        if (isBusy) return;
        editingId = rule.Id;
        deletingId = null;
        editDay = rule.DayOfMonth;
        editAmount = rule.Amount;
        editCategory = rule.Category;
        editMemo = rule.Memo;
        errorMessage = null;
        notice = null;
    }

    private void CancelEdit() => editingId = null;

    private async Task SaveEditAsync()
    {
        if (isBusy || editingId is not int id || !Validate(editDay, editAmount, editCategory, editMemo) || !await CheckOwnerAsync())
            return;
        isBusy = true;
        try
        {
            if (!await RecurringExpenseService.UpdateAsync(ownerId, id, editDay, editAmount, editCategory, editMemo))
                throw new InvalidOperationException("규칙을 찾을 수 없습니다.");
            await RecurringExpenseService.GenerateDueAsync(ownerId, DateTime.Today);
            await ReloadAsync();
            editingId = null;
            errorMessage = null;
            notice = "규칙을 수정했습니다. 이미 생성된 지출은 바뀌지 않습니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "정기 지출 수정에 실패했습니다.");
            errorMessage = "수정 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
        }
        finally { isBusy = false; }
    }

    private async Task ToggleAsync(RecurringExpenseRule rule)
    {
        if (isBusy || !await CheckOwnerAsync()) return;
        isBusy = true;
        try
        {
            if (!await RecurringExpenseService.SetActiveAsync(ownerId, rule.Id, !rule.IsActive))
                throw new InvalidOperationException("규칙을 찾을 수 없습니다.");
            if (!rule.IsActive)
                await RecurringExpenseService.GenerateDueAsync(ownerId, DateTime.Today);
            await ReloadAsync();
            errorMessage = null;
            notice = rule.IsActive ? "규칙을 중지했습니다." : "규칙을 다시 시작했습니다. 중지 기간의 지출은 생성하지 않습니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "정기 지출 상태 변경에 실패했습니다.");
            errorMessage = "상태 변경 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
        }
        finally { isBusy = false; }
    }

    private void ShowDelete(int id) { deletingId = id; editingId = null; notice = null; }
    private void CancelDelete() => deletingId = null;

    private async Task DeleteAsync(int id)
    {
        if (isBusy || deletingId != id || !await CheckOwnerAsync()) return;
        isBusy = true;
        try
        {
            if (!await RecurringExpenseService.DeleteAsync(ownerId, id))
                throw new InvalidOperationException("규칙을 찾을 수 없습니다.");
            await ReloadAsync();
            deletingId = null;
            errorMessage = null;
            notice = "규칙을 삭제했습니다. 기존 지출은 유지됩니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "정기 지출 삭제에 실패했습니다.");
            errorMessage = "삭제 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
        }
        finally { isBusy = false; }
    }
}
