using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Income
{
    private string ownerId = "";
    private DateTime date = KoreanClock.Today;
    private long amount;
    private string source = IncomeRecord.Sources[0];
    private string memo = "";
    private DateTime month = new(KoreanClock.Today.Year, KoreanClock.Today.Month, 1);
    private string monthInput = KoreanClock.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private List<IncomeRecord> incomes = [];
    private MonthlyCashflow cashflow = new(0, 0);
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
        var list = await IncomeService.ListAsync(ownerId, month);
        var summary = await IncomeService.CashflowAsync(ownerId, month);
        await CheckOwnerAsync();
        incomes = list;
        cashflow = summary;
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            await GenerateRecurringAsync();
            await ReloadAsync();
            error = null;
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; loading = false; }
    }

    // 도래한 정기 수입을 채웁니다. 실패해도 수입 목록 조회는 계속합니다.
    private async Task GenerateRecurringAsync()
    {
        await CheckOwnerAsync();
        try
        {
            await RecurringIncomeService.GenerateDueAsync(ownerId, KoreanClock.Today);
        }
        catch (Exception ex) { Logger.LogWarning(ex, "정기 수입을 생성하지 못했습니다."); }
    }

    private async Task MonthChangedAsync(ChangeEventArgs args)
    {
        if (busy) return;
        if (!DateTime.TryParseExact(args.Value?.ToString(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected))
        { error = "조회할 월을 선택해 주세요."; return; }
        var previous = month;
        month = selected;
        busy = true;
        try { await ReloadAsync(); monthInput = selected.ToString("yyyy-MM", CultureInfo.InvariantCulture); error = null; deletingId = null; }
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
            var input = new IncomeInput(date, amount, source, memo ?? "");
            if (editingId is int id)
            {
                if (await IncomeService.UpdateAsync(ownerId, id, input) is null)
                    throw new ArgumentException("수정할 수입 기록이 없습니다. 새로고침해 주세요.");
            }
            else
            {
                await IncomeService.AddAsync(ownerId, input);
            }

            // 저장한 달로 이동해 방금 입력한 기록이 목록에 바로 보이게 합니다.
            month = new DateTime(date.Year, date.Month, 1);
            monthInput = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var wasEditing = editingId is not null;
            CancelEdit();
            await ReloadAsync();
            error = null;
            notice = wasEditing ? "수입 기록을 수정했습니다." : "수입을 기록했습니다.";
        }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private void Edit(IncomeRecord income)
    {
        if (busy) return;
        editingId = income.Id;
        deletingId = null;
        date = income.Date;
        amount = income.Amount;
        source = income.Source;
        memo = income.Memo;
        notice = error = null;
    }

    private void CancelEdit()
    {
        editingId = null;
        date = KoreanClock.Today;
        amount = 0;
        source = IncomeRecord.Sources[0];
        memo = "";
    }

    private async Task DeleteAsync(int id)
    {
        if (busy || deletingId != id) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await IncomeService.DeleteAsync(ownerId, id);
            if (editingId == id) CancelEdit();
            deletingId = null;
            await ReloadAsync();
            error = null;
            notice = "수입 기록을 삭제했습니다.";
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private void HandleError(Exception ex)
    {
        Logger.LogError(ex, "수입 기록 처리 실패");
        error = "처리 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
    }
}
