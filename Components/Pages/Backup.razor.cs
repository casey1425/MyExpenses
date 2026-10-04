using System.Security.Claims;
using Microsoft.AspNetCore.Components.Forms;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Backup
{
    private const string ReplacePhrase = "덮어쓰기";
    private string ownerId = string.Empty;
    private BackupFile? backup;
    private BackupRestoreReport? preview;
    private BackupRestoreReport? result;
    private IReadOnlyList<string> issues = [];
    private string? errorMessage;
    private string? fileName;
    private bool busy;
    private int fileInputVersion;
    private BackupRestoreMode mode = BackupRestoreMode.Merge;
    private string confirmText = string.Empty;

    private bool CanApply => backup is not null && preview is not null &&
        (mode == BackupRestoreMode.Merge || confirmText.Trim() == ReplacePhrase);

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = state.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    private async Task CheckOwnerAsync()
    {
        var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (ownerId == "" || user.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            throw new InvalidOperationException("계정이 변경되었습니다.");
    }

    private void Reset()
    {
        backup = null;
        preview = null;
        result = null;
        issues = [];
        errorMessage = null;
        fileName = null;
        confirmText = string.Empty;
        mode = BackupRestoreMode.Merge;
    }

    private async Task ReadFileAsync(InputFileChangeEventArgs args)
    {
        if (busy) return;
        Reset();
        busy = true;
        try
        {
            var file = args.File;
            if (!file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "MyExpenses에서 내려받은 .json 백업 파일을 선택해 주세요.";
                return;
            }

            await using var stream = file.OpenReadStream(BackupLimits.MaxFileBytes);
            await LoadAsync(stream, file.Name);
        }
        catch (IOException)
        {
            issues = [$"파일이 너무 큽니다. 최대 {BackupLimits.MaxFileBytes / 1024 / 1024}MB까지 복원할 수 있습니다."];
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            busy = false;
            fileInputVersion++;
        }
    }

    // 파일을 읽어 검증하고 미리보기를 만듭니다. 브라우저 파일 선택과 분리해 두어 직접 호출해 검증할 수 있습니다.
    private async Task LoadAsync(Stream stream, string name)
    {
        await CheckOwnerAsync();
        var bytes = await ReadBoundedAsync(stream);
        if (bytes is null)
        {
            issues = [$"파일이 너무 큽니다. 최대 {BackupLimits.MaxFileBytes / 1024 / 1024}MB까지 복원할 수 있습니다."];
            return;
        }

        var parsed = BackupSerializer.Parse(bytes);
        if (parsed.File is null)
        {
            issues = parsed.Issues;
            return;
        }

        backup = parsed.File;
        fileName = name;
        await RefreshPreviewAsync();
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > BackupLimits.MaxFileBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    // 실제 복원과 같은 코드를 실행한 뒤 되돌려, 미리보기가 실제 결과와 같게 합니다.
    private async Task RefreshPreviewAsync()
    {
        preview = null;
        if (backup is null) return;
        try
        {
            await CheckOwnerAsync();
            preview = await BackupService.RestoreAsync(ownerId, backup, mode, dryRun: true);
            errorMessage = null;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
    }

    private async Task ChangeModeAsync(BackupRestoreMode next)
    {
        if (busy || next == mode) return;
        mode = next;
        confirmText = string.Empty;
        busy = true;
        try { await RefreshPreviewAsync(); }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private async Task ApplyAsync()
    {
        if (busy || backup is null || preview is null) return;
        if (mode == BackupRestoreMode.Replace && confirmText.Trim() != ReplacePhrase)
        {
            errorMessage = $"덮어쓰기를 진행하려면 확인 문구 ‘{ReplacePhrase}’를 정확히 입력해 주세요.";
            return;
        }

        busy = true;
        errorMessage = null;
        try
        {
            await CheckOwnerAsync();
            result = await BackupService.RestoreAsync(ownerId, backup, mode, dryRun: false);
            backup = null;
            preview = null;
            confirmText = string.Empty;
            fileName = null;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            busy = false;
        }
    }

    private void HandleError(Exception ex)
    {
        Logger.LogError(ex, "백업·복원 처리 실패");
        errorMessage = "처리하지 못했습니다. 아무것도 변경되지 않았으니 잠시 후 다시 시도해 주세요.";
    }
}
