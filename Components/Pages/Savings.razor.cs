using System.Globalization;
using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Savings
{
    private string ownerId = "";
    private DateTime today = KoreanClock.Today;
    private List<SavingsGoalSummary> goals = [];
    private long? averageNet;
    private bool loading = true;
    private bool busy;
    private string? error;
    private string? notice;

    private string goalName = "";
    private long goalTarget;
    private DateTime? goalDate;
    private int? editingId;
    private int? deletingGoalId;

    private int? depositGoalId;
    private DateTime depositDate = KoreanClock.Today;
    private long depositAmount;
    private string depositMemo = "";
    private int? historyGoalId;
    private List<SavingsDeposit> deposits = [];
    private int? deletingDepositId;

    private long totalSaved => goals.Sum(item => item.Progress.Saved);
    private long totalTarget => goals.Sum(item => item.Goal.TargetAmount);
    // 기한이 지나지 않은 목표들의 월 필요 저축액 합계입니다. 기한이 지난 목표는 한 번에 필요한 금액이라 제외합니다.
    private long requiredTotal => goals.Where(item => !item.Progress.IsOverdue && !item.Progress.IsAchieved)
        .Sum(item => item.Progress.RequiredPerMonth ?? 0);
    private int overdueCount => goals.Count(item => item.Progress.IsOverdue);

    private string OverviewMessage
    {
        get
        {
            var overdue = overdueCount > 0 ? $" 기한이 지난 목표 {overdueCount}개는 합계에서 제외했습니다." : "";
            if (averageNet is not long net)
                return "최근 3개월(이번 달 제외)의 수입·지출 기록이 없어 비교할 수 없어요." + overdue;
            if (requiredTotal == 0)
                return "목표일이 있는 진행 중 목표가 없어 필요 월 저축액을 비교하지 않아요." + overdue;
            return net >= requiredTotal
                ? "최근 순수지 수준이면 목표일 안에 모두 달성할 수 있어요." + overdue
                : $"최근 순수지로는 목표 달성에 월 {requiredTotal - Math.Max(net, 0):N0}원이 부족해요. 목표일이나 금액을 조정해 보세요." + overdue;
        }
    }

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
        today = KoreanClock.Today;
        var list = await SavingsGoalService.ListAsync(ownerId, today);
        var net = await SavingsGoalService.AverageMonthlyNetAsync(ownerId, today);
        List<SavingsDeposit>? history = historyGoalId is int goalId && list.Any(item => item.Goal.Id == goalId)
            ? await SavingsGoalService.ListDepositsAsync(ownerId, goalId)
            : null;
        await CheckOwnerAsync();
        goals = list;
        averageNet = net;
        if (history is null) { historyGoalId = null; deposits = []; }
        else deposits = history;
    }

    private async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try { await ReloadAsync(); error = null; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; loading = false; }
    }

    private async Task SaveGoalAsync()
    {
        if (busy) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            var now = KoreanClock.Today;
            if (editingId is int id)
            {
                if (!await SavingsGoalService.UpdateAsync(ownerId, id, now, goalName, goalTarget, goalDate))
                    throw new ArgumentException("수정할 목표가 없습니다. 새로고침해 주세요.");
                notice = "목표를 수정했습니다.";
            }
            else
            {
                await SavingsGoalService.CreateAsync(ownerId, now, goalName, goalTarget, goalDate);
                notice = "목표를 만들었습니다. 저축하기로 첫 저축을 기록해 보세요.";
            }
            CancelEdit();
            await ReloadAsync();
            error = null;
        }
        catch (ArgumentException ex) { error = ex.Message; notice = null; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private void StartEdit(SavingsGoal goal)
    {
        if (busy) return;
        editingId = goal.Id;
        deletingGoalId = null;
        depositGoalId = null;
        goalName = goal.Name;
        goalTarget = goal.TargetAmount;
        goalDate = goal.TargetDate;
        notice = error = null;
    }

    private void CancelEdit()
    {
        editingId = null;
        goalName = "";
        goalTarget = 0;
        goalDate = null;
    }

    private void StartDeposit(int goalId)
    {
        if (busy) return;
        depositGoalId = goalId;
        deletingGoalId = null;
        depositDate = KoreanClock.Today;
        depositAmount = 0;
        depositMemo = "";
        notice = error = null;
    }

    private async Task SaveDepositAsync()
    {
        if (busy || depositGoalId is not int goalId) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await SavingsGoalService.AddDepositAsync(ownerId, goalId, KoreanClock.Today, depositDate, depositAmount, depositMemo ?? "");
            depositGoalId = null;
            await ReloadAsync();
            error = null;
            notice = "저축을 기록했습니다.";
        }
        catch (ArgumentException ex) { error = ex.Message; }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private async Task ToggleHistoryAsync(int goalId)
    {
        if (busy) return;
        if (historyGoalId == goalId) { historyGoalId = null; deposits = []; deletingDepositId = null; return; }
        busy = true;
        try
        {
            await CheckOwnerAsync();
            deposits = await SavingsGoalService.ListDepositsAsync(ownerId, goalId);
            historyGoalId = goalId;
            deletingDepositId = null;
            error = null;
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private async Task DeleteDepositAsync(int depositId)
    {
        if (busy || deletingDepositId != depositId) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await SavingsGoalService.DeleteDepositAsync(ownerId, depositId);
            deletingDepositId = null;
            await ReloadAsync();
            error = null;
            notice = "저축 내역을 삭제했습니다.";
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private async Task DeleteGoalAsync(int id)
    {
        if (busy || deletingGoalId != id) return;
        busy = true;
        notice = null;
        try
        {
            await CheckOwnerAsync();
            await SavingsGoalService.DeleteAsync(ownerId, id);
            if (editingId == id) CancelEdit();
            if (depositGoalId == id) depositGoalId = null;
            if (historyGoalId == id) { historyGoalId = null; deposits = []; }
            deletingGoalId = null;
            await ReloadAsync();
            error = null;
            notice = "목표를 삭제했습니다.";
        }
        catch (Exception ex) { HandleError(ex); }
        finally { busy = false; }
    }

    private static string StatusText(SavingsGoalProgress progress) =>
        progress.IsAchieved ? "달성 완료" : progress.IsOverdue ? "기한 지남" : "진행 중";

    private static string StatusClass(SavingsGoalProgress progress) =>
        progress.IsAchieved ? "ok" : progress.IsOverdue ? "late" : "";

    private static string DaysText(SavingsGoalProgress progress) => progress.DaysLeft switch
    {
        null => "",
        0 => "오늘까지",
        > 0 => $"D-{progress.DaysLeft}",
        _ => $"{-progress.DaysLeft}일 지남"
    };

    private void HandleError(Exception ex)
    {
        Logger.LogError(ex, "목표 저축 처리 실패");
        error = "처리 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
    }
}
