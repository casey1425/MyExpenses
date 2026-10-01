using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Home
{
    private async Task LoadExpensesAsync()
    {
        await CheckOwnerAsync();
        await LoadCategoriesAsync();
        var loaded = await ExpenseService.ListAsync(ownerId, activeFilter);
        await CheckOwnerAsync();
        var statisticsExpenses = activeFilter.IsActive
            ? loaded
            : loaded.Where(item => item.Date.Year == DateTime.Today.Year && item.Date.Month == DateTime.Today.Month);
        var statistics = ExpenseStatistics.ByCategory(statisticsExpenses);
        expenses = loaded;
        categoryStatistics = statistics;
    }

    private async Task ApplyFiltersAsync()
    {
        if (isSearching) return;
        if (!searchInput.TryCreate(out var nextFilter, out var validationError))
        {
            filterError = validationError;
            return;
        }

        var previousFilter = activeFilter;
        activeFilter = nextFilter;
        isSearching = true;

        try
        {
            await CheckOwnerAsync();
            await ExpenseService.ValidatePaymentAsync(ownerId, nextFilter.PaymentMethodId);
            await LoadExpensesAsync();
            filterError = null;
            deleteError = null;
            saveNotice = null;
            historyNotice = null;
            showDeleteAllConfirmation = false;
            CancelEdit();
        }
        catch (ArgumentException ex)
        {
            activeFilter = previousFilter;
            filterError = ex.Message;
        }
        catch (Exception ex)
        {
            activeFilter = previousFilter;
            Logger.LogError(ex, "지출 내역 필터 조회에 실패했습니다.");
            filterError = "지출 내역을 조회하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            isSearching = false;
        }
    }

    private async Task ResetFiltersAsync()
    {
        if (isSearching) return;
        searchInput = new();
        await ApplyFiltersAsync();
    }

    private async Task AddExpenseAsync()
    {
        if (templateBusy) return;
        if (amount <= 0)
        {
            errorMessage = "금액은 1원 이상의 정수로 입력해 주세요.";
            return;
        }

        if (!categories.Contains(category))
        {
            errorMessage = "카테고리를 선택해 주세요.";
            return;
        }

        try
        {
            await CheckOwnerAsync();
            var newExpense = await ExpenseService.AddAsync(ownerId, new(expenseDate, amount, category, memo, paymentMethodId));
            await LoadExpensesAsync();
            await RefreshBudgetAsync();

            amount = 0;
            memo = string.Empty;
            selectedTemplateId = "";
            templateNotice = null;
            paymentMethodId = null;
            errorMessage = null;
            saveNotice = activeFilter.IsActive && !activeFilter.Matches(newExpense)
                ? "저장했습니다. 현재 조회 조건에 맞지 않아 목록에는 표시되지 않습니다."
                : null;
            showDeleteAllConfirmation = false;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "지출 내역을 저장하지 못했습니다.");
            errorMessage = "지출 내역을 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private void StartEdit(ExpenseRecord expense)
    {
        if (isSavingEdit)
            return;

        editingId = expense.Id;
        editDate = expense.Date;
        editAmount = expense.Amount;
        editCategory = expense.Category;
        editMemo = expense.Memo;
        editPaymentMethodId = expense.PaymentMethodId;
        editError = null;
        historyNotice = null;
        deleteError = null;
        showDeleteAllConfirmation = false;
    }

    private void CancelEdit()
    {
        editingId = null;
        editError = null;
    }

    private async Task SaveEditAsync()
    {
        if (editingId is not int id || isSavingEdit)
            return;

        if (editDate == default || editAmount <= 0)
        {
            editError = "날짜와 1원 이상의 금액을 입력해 주세요.";
            return;
        }

        if (!EditCategories.Contains(editCategory) || editMemo.Length > 100)
        {
            editError = "카테고리와 메모를 다시 확인해 주세요.";
            return;
        }

        isSavingEdit = true;
        try
        {
            await CheckOwnerAsync();
            var expense = await ExpenseService.UpdateAsync(ownerId, id, new(editDate, editAmount, editCategory, editMemo, editPaymentMethodId));
            if (expense is null)
            {
                await LoadExpensesAsync();
                CancelEdit();
                historyNotice = "수정할 기록을 찾을 수 없어 목록을 새로고침했습니다.";
                return;
            }

            await LoadExpensesAsync();
            await RefreshBudgetAsync();

            historyNotice = activeFilter.IsActive && !activeFilter.Matches(expense)
                ? "수정했습니다. 변경된 기록은 현재 조회 조건에 맞지 않아 목록에서 보이지 않습니다."
                : "지출 기록을 수정했습니다.";
            CancelEdit();
        }
        catch (ArgumentException ex)
        {
            editError = ex.Message;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "지출 내역을 수정하지 못했습니다.");
            editError = "수정 결과를 확인하지 못했습니다. 새로고침 후 다시 확인해 주세요.";
        }
        finally
        {
            isSavingEdit = false;
        }
    }

    private async Task DeleteExpenseAsync(int id)
    {
        try
        {
            await CheckOwnerAsync();
            if (!await ExpenseService.DeleteAsync(ownerId, id))
            {
                await LoadExpensesAsync();
                return;
            }

            await LoadExpensesAsync();
            await RefreshBudgetAsync();
            deleteError = null;
            historyNotice = null;
            showDeleteAllConfirmation = false;
            if (editingId == id)
                CancelEdit();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "지출 내역을 삭제하지 못했습니다.");
            deleteError = "지출 내역을 삭제하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private void ShowDeleteAllConfirmation()
    {
        if (activeFilter.IsActive || expenses.Count == 0 || editingId is not null)
            return;

        deleteError = null;
        showDeleteAllConfirmation = true;
    }

    private void CancelDeleteAll()
    {
        showDeleteAllConfirmation = false;
    }

    private async Task DeleteAllExpensesAsync()
    {
        if (!showDeleteAllConfirmation || isDeletingAll || activeFilter.IsActive || editingId is not null || expenses.Count == 0)
            return;

        isDeletingAll = true;
        try
        {
            await CheckOwnerAsync();
            await ExpenseService.DeleteAllAsync(ownerId);
            await LoadExpensesAsync();
            await RefreshBudgetAsync();
            showDeleteAllConfirmation = false;
            deleteError = null;
            historyNotice = null;
            CancelEdit();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "전체 지출 내역을 삭제하지 못했습니다.");
            deleteError = "전체 삭제에 실패했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            isDeletingAll = false;
        }
    }

    private static string CategoryIcon(string value) => ExpenseCategories.Icon(value);
}
