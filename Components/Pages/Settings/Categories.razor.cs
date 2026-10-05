using System.Security.Claims;
using MyExpenses.Data;

namespace MyExpenses.Components.Pages;

public partial class Categories
{
    private string ownerId = "";
    private string name = "";
    private int? editingId;
    private List<UserCategory> items = [];
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
            throw new ArgumentException("로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.");
    }

    private async Task ReloadAsync()
    {
        await CheckOwnerAsync();
        var loaded = await CategoryService.ListAsync(ownerId);
        await CheckOwnerAsync();
        items = loaded;
        loading = false;
    }

    private async Task RunAsync(Func<Task> action, string? message = null)
    {
        if (busy) return;
        busy = true;
        notice = error = null;
        try
        {
            await CheckOwnerAsync();
            await action();
            await ReloadAsync();
            notice = message;
        }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "카테고리 처리 실패");
            error = "처리 결과를 확인하지 못했습니다. 새로고침 후 다시 확인해 주세요.";
        }
        finally { busy = false; loading = false; }
    }

    private Task RefreshAsync() => RunAsync(() => Task.CompletedTask);
    private Task SaveAsync() => RunAsync(async () =>
    {
        await CategoryService.SaveAsync(ownerId, editingId, name);
        CancelEdit();
    }, "카테고리를 저장했습니다. 다른 탭의 목록은 새로고침해 주세요.");

    private Task ToggleAsync(UserCategory item) => RunAsync(
        () => CategoryService.ArchiveAsync(ownerId, item.Id, !item.IsArchived),
        item.IsArchived ? "카테고리를 복원했습니다." : "카테고리를 보관했습니다. 기존 데이터와 정기 지출은 유지됩니다.");

    private Task MoveAsync(int id, int direction) => RunAsync(
        () => CategoryService.MoveAsync(ownerId, id, direction), "표시 순서를 변경했습니다.");

    private void Edit(UserCategory item)
    {
        if (busy) return;
        editingId = item.Id;
        name = item.Name;
        error = notice = null;
    }

    private void CancelEdit() { editingId = null; name = ""; }
}
