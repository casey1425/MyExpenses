using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Tags
{
    private string ownerId = string.Empty;
    private List<TagUsage> tags = [];
    private int? editingId;
    private string editName = string.Empty;
    private int? deletingId;
    private string? conflictSource;
    private string? conflictTarget;
    private string? errorMessage;
    private string? notice;
    private bool isLoading = true;
    private bool isBusy;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await ReloadAsync();
    }

    private async Task CheckOwnerAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (string.IsNullOrEmpty(ownerId) || state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            throw new InvalidOperationException("로그인 계정이 바뀌었습니다.");
    }

    private async Task ReloadAsync()
    {
        try
        {
            await CheckOwnerAsync();
            var loaded = await TagService.ListAsync(ownerId);
            await CheckOwnerAsync();
            tags = loaded;
            errorMessage = null;
        }
        catch (InvalidOperationException)
        {
            tags = [];
            errorMessage = "로그인 상태가 바뀌었습니다. 페이지를 새로고침해 주세요.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "태그 목록을 불러오지 못했습니다.");
            errorMessage = "태그를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isLoading = false; }
    }

    private void StartRename(TagUsage tag)
    {
        if (isBusy) return;
        editingId = tag.Id;
        editName = tag.Name;
        deletingId = null;
        ClearMessages();
    }

    private void CancelRename()
    {
        editingId = null;
        ClearMessages();
    }

    private void CancelConflict() => ClearMessages();

    private void ClearMessages()
    {
        conflictSource = conflictTarget = null;
        errorMessage = notice = null;
    }

    private async Task SaveRenameAsync(bool merge)
    {
        if (isBusy || editingId is not int id) return;
        isBusy = true;
        try
        {
            await CheckOwnerAsync();
            var source = tags.FirstOrDefault(tag => tag.Id == id)?.Name ?? string.Empty;
            var result = await TagService.RenameAsync(ownerId, id, editName, merge);
            switch (result)
            {
                case TagRenameResult.Conflict:
                    conflictSource = source;
                    conflictTarget = TagNames.Clean(editName);
                    errorMessage = null;
                    notice = null;
                    return;
                case TagRenameResult.NotFound:
                    // 목록을 다시 읽으면 오류 문구가 지워지므로, 먼저 읽고 안내를 남깁니다.
                    conflictSource = conflictTarget = null;
                    editingId = null;
                    await ReloadAsync();
                    errorMessage = "태그를 찾을 수 없어 목록을 새로고침했습니다.";
                    return;
                case TagRenameResult.Merged:
                    notice = $"‘{source}’ 태그를 ‘{TagNames.Clean(editName)}’(으)로 합쳤습니다.";
                    conflictSource = conflictTarget = null;
                    errorMessage = null;
                    break;
                default:
                    notice = "태그 이름을 바꿨습니다.";
                    conflictSource = conflictTarget = null;
                    errorMessage = null;
                    break;
            }
            editingId = null;
            await ReloadAsync();
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
        catch (InvalidOperationException)
        {
            errorMessage = "로그인 상태가 바뀌었습니다. 페이지를 새로고침해 주세요.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "태그 이름을 바꾸지 못했습니다.");
            errorMessage = "태그 이름을 바꾸지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isBusy = false; }
    }

    private void AskDelete(TagUsage tag)
    {
        if (isBusy) return;
        deletingId = tag.Id;
        editingId = null;
        ClearMessages();
    }

    private void CancelDelete() => deletingId = null;

    private async Task DeleteAsync(TagUsage tag)
    {
        if (isBusy || deletingId != tag.Id) return;
        isBusy = true;
        try
        {
            await CheckOwnerAsync();
            var deleted = await TagService.DeleteAsync(ownerId, tag.Id);
            notice = deleted ? $"‘{tag.Name}’ 태그를 지웠습니다." : "이미 지워진 태그라 목록을 새로고침했습니다.";
            errorMessage = null;
            deletingId = null;
            await ReloadAsync();
        }
        catch (InvalidOperationException)
        {
            errorMessage = "로그인 상태가 바뀌었습니다. 페이지를 새로고침해 주세요.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "태그를 지우지 못했습니다.");
            errorMessage = "태그를 지우지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally { isBusy = false; }
    }
}
