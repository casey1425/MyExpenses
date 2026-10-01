using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Import
{
    private const long MaxFileBytes = 1_048_576;
    private string ownerId = string.Empty;
    private ExpenseCsvParseResult? parseResult;
    private IReadOnlyList<ExpenseCsvCandidate> candidates = [];
    private string? errorMessage;
    private string? successMessage;
    private bool includeDuplicates;
    private bool isReading;
    private bool isSaving;
    private int fileInputVersion;
    private int DuplicateCount => candidates.Count(candidate => candidate.IsDuplicate);

    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        ownerId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    }

    private async Task ReadFileAsync(InputFileChangeEventArgs args)
    {
        if (isReading || isSaving)
            return;

        parseResult = null;
        candidates = [];
        errorMessage = null;
        successMessage = null;
        includeDuplicates = false;
        isReading = true;

        try
        {
            if (string.IsNullOrEmpty(ownerId))
            {
                errorMessage = "로그인 상태를 확인할 수 없습니다. 다시 로그인해 주세요.";
                return;
            }

            var file = args.File;
            if (!file.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "CSV 파일을 선택해 주세요.";
                return;
            }

            await using var input = file.OpenReadStream(MaxFileBytes);
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer);
            buffer.Position = 0;
            using var reader = new StreamReader(buffer, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true);
            parseResult = ExpenseCsvImporter.Parse(reader);
            if (parseResult.Rows.Count > 0)
                candidates = await ImportService.PreviewAsync(ownerId, parseResult.Rows);
        }
        catch (ArgumentException ex)
        {
            parseResult = null;
            candidates = [];
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "CSV 파일 미리보기를 완료하지 못했습니다.");
            parseResult = null;
            candidates = [];
            errorMessage = "CSV를 읽지 못했습니다. UTF-8 CSV인지, 1MB 이하인지 확인해 주세요.";
        }
        finally
        {
            isReading = false;
            fileInputVersion++;
        }
    }

    private async Task SaveAsync()
    {
        if (isSaving || isReading || parseResult is null || parseResult.Issues.Count > 0 ||
            candidates.Count == 0 || string.IsNullOrEmpty(ownerId))
            return;

        isSaving = true;
        errorMessage = null;
        try
        {
            var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            if (authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
            {
                errorMessage = "로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.";
                return;
            }

            var count = await ImportService.ImportAsync(ownerId, parseResult.Rows, includeDuplicates);
            successMessage = count == 0
                ? "새로 가져온 기록이 없습니다. 모두 기존 기록과 중복됩니다."
                : $"지출 {count:N0}건을 가져왔습니다.";
            parseResult = null;
            candidates = [];
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "CSV 지출 가져오기에 실패했습니다.");
            errorMessage = "가져오기에 실패했습니다. 새 기록이 저장되지 않았는지 확인한 뒤 다시 시도해 주세요.";
        }
        finally
        {
            isSaving = false;
        }
    }
}
