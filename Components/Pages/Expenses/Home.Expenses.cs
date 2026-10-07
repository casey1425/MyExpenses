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
            : loaded.Where(item => item.Date.Year == KoreanClock.Today.Year && item.Date.Month == KoreanClock.Today.Month);
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
            lastDeleted = null;
            visibleCount = PageSize;
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

    private void ShowMore() => visibleCount = Math.Min(Math.Max(visibleCount, PageSize) + PageSize, Math.Max(expenses.Count, PageSize));

    private void ShowAll() => visibleCount = Math.Max(expenses.Count, PageSize);

    // 새로 저장하거나 되돌린 기록이 아직 보이지 않는 위치라면 그 기록까지 보이게 넓힙니다.
    private void ShowThrough(int expenseId)
    {
        var index = expenses.FindIndex(item => item.Id == expenseId);
        if (index >= visibleCount) visibleCount = index + 1;
    }

    private async Task ShiftMonthAsync(int delta)
    {
        var baseMonth = activeFilter.Month ?? KoreanClock.Today;
        DateTime target;
        try { target = new DateTime(baseMonth.Year, baseMonth.Month, 1).AddMonths(delta); }
        catch (ArgumentOutOfRangeException) { return; }
        await ShowMonthAsync(target);
    }

    private Task ShowThisMonthAsync() => ShowMonthAsync(KoreanClock.Today);

    private async Task ShowMonthAsync(DateTime month)
    {
        if (isSearching || month.Year < 2) return;
        searchInput.Month = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await ApplyFiltersAsync();
    }

    private async Task LoadTagsAsync()
    {
        try
        {
            var loaded = await TagService.ListAsync(ownerId);
            await CheckOwnerAsync();
            tagUsage = loaded;
        }
        catch (Exception ex)
        {
            // 태그 목록은 편의 기능이므로 실패해도 지출 입력을 막지 않습니다.
            Logger.LogWarning(ex, "태그 목록을 불러오지 못했습니다.");
        }
    }

    // 추천 태그를 누르면 입력 칸에 넣고, 이미 있으면 뺍니다.
    private void ToggleTag(string name)
    {
        IReadOnlyList<string> current;
        try { current = TagNames.Parse(tagsText); }
        catch (ArgumentException ex) { errorMessage = ex.Message; return; }

        var key = TagNames.Key(name);
        var next = current.Where(item => TagNames.Key(item) != key).ToList();
        if (next.Count == current.Count)
        {
            if (current.Count >= TagNames.MaxPerExpense)
            {
                errorMessage = $"태그는 지출 하나에 {TagNames.MaxPerExpense}개까지 붙일 수 있습니다.";
                return;
            }
            next.Add(name);
        }
        errorMessage = null;
        tagsText = TagNames.Join(next);
    }

    private async Task ShowTagAsync(int tagId)
    {
        if (isSearching) return;
        searchInput.Tag = tagId.ToString(CultureInfo.InvariantCulture);
        await ApplyFiltersAsync();
    }

    private async Task LoadSuggestionsAsync()
    {
        try
        {
            var loaded = await ExpenseService.SuggestionsAsync(ownerId, KoreanClock.Today);
            await CheckOwnerAsync();
            memoSuggestions = loaded;
        }
        catch (Exception ex)
        {
            // 자동 완성은 편의 기능이므로 실패해도 입력을 막지 않습니다.
            Logger.LogWarning(ex, "메모 자동 완성 목록을 불러오지 못했습니다.");
        }
    }

    // 이전에 쓴 메모를 입력하면 그 메모로 마지막에 기록한 카테고리·결제수단을 채우고, 금액이 비어 있으면 금액도 채웁니다.
    private void OnMemoChanged(string value)
    {
        memo = value ?? string.Empty;
        autofillNotice = null;
        var match = memoSuggestions.FirstOrDefault(item => string.Equals(item.Memo, memo.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        var filled = new List<string>();
        if (categories.Contains(match.Category) && category != match.Category)
        {
            category = match.Category;
            filled.Add(match.Category);
        }
        if (match.PaymentMethodId is int methodId && paymentMethodId != methodId && paymentMethods.Any(m => m.Id == methodId))
        {
            paymentMethodId = methodId;
            filled.Add(paymentMethods.First(m => m.Id == methodId).Name);
        }
        if (amount <= 0)
        {
            amount = match.Amount;
            filled.Add($"{match.Amount:N0}원");
        }
        if (filled.Count > 0) autofillNotice = $"이전 ‘{match.Memo}’ 기록을 따라 {string.Join(" · ", filled)}을(를) 채웠어요. 다르면 바꿔 주세요.";
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
            var newExpense = await ExpenseService.AddAsync(ownerId, new(expenseDate, amount, category, memo, paymentMethodId, TagNames.Parse(tagsText)));
            await LoadExpensesAsync();
            ShowThrough(newExpense.Id);
            await LoadTagsAsync();
            await RefreshBudgetAsync();

            // 연속 입력을 위해 날짜·카테고리·결제수단·태그는 남기고 금액과 메모만 비웁니다(여행 중 여러 건 입력 등).
            amount = 0;
            memo = string.Empty;
            selectedTemplateId = "";
            templateNotice = null;
            autofillNotice = null;
            lastDeleted = null;
            errorMessage = null;
            if (activeFilter.IsActive && !activeFilter.Matches(newExpense))
                saveNotice = "저장했습니다. 현재 조회 조건에 맞지 않아 목록에는 표시되지 않습니다.";
            else
                ShowSavedToast($"✓ {newExpense.Category} {newExpense.Amount:N0}원을 저장했습니다.");
            showDeleteAllConfirmation = false;
            await LoadSuggestionsAsync();
            if (entryForm is not null) await entryForm.FocusAmountAsync();
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

    // 저장 확인 문구는 몇 초 뒤에 저절로 사라집니다. 그 사이 다른 안내로 바뀌었다면 건드리지 않습니다.
    private void ShowSavedToast(string text)
    {
        saveNotice = text;
        toastTimer?.Cancel();
        toastTimer = new CancellationTokenSource();
        _ = ClearSavedToastAsync(text, toastTimer.Token);
    }

    private async Task ClearSavedToastAsync(string text, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(ToastDuration, cancellation);
            await InvokeAsync(() =>
            {
                if (saveNotice == text)
                {
                    saveNotice = null;
                    StateHasChanged();
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
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
        editTagsText = TagNames.Join(expense.TagNames);
        editError = null;
        historyNotice = null;
        deleteError = null;
        lastDeleted = null;
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
            var expense = await ExpenseService.UpdateAsync(ownerId, id, new(editDate, editAmount, editCategory, editMemo, editPaymentMethodId, TagNames.Parse(editTagsText)));
            if (expense is null)
            {
                await LoadExpensesAsync();
                CancelEdit();
                historyNotice = "수정할 기록을 찾을 수 없어 목록을 새로고침했습니다.";
                return;
            }

            await LoadExpensesAsync();
            ShowThrough(expense.Id);
            await LoadTagsAsync();
            await RefreshBudgetAsync();
            lastDeleted = null;

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
        var target = expenses.FirstOrDefault(item => item.Id == id);
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
            // 방금 지운 기록을 되돌릴 수 있게 내용을 기억해 둡니다. 다른 작업을 하면 사라집니다.
            lastDeleted = target is null ? null : new ExpenseInput(target.Date, target.Amount, target.Category, target.Memo, target.PaymentMethodId, target.TagNames);
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

    private async Task UndoDeleteAsync()
    {
        if (lastDeleted is not ExpenseInput input || undoBusy) return;
        undoBusy = true;
        try
        {
            await CheckOwnerAsync();
            var restored = await ExpenseService.AddAsync(ownerId, input);
            lastDeleted = null;
            await LoadExpensesAsync();
            ShowThrough(restored.Id);
            await LoadTagsAsync();
            await RefreshBudgetAsync();
            await LoadSuggestionsAsync();
            deleteError = null;
            historyNotice = activeFilter.IsActive && !activeFilter.Matches(restored)
                ? "되돌렸습니다. 현재 조회 조건에 맞지 않아 목록에는 표시되지 않습니다."
                : "삭제한 지출을 되돌렸습니다.";
        }
        catch (ArgumentException ex)
        {
            // 그 사이 카테고리를 보관했거나 결제수단을 삭제한 경우처럼 다시 시도해도 같은 결과입니다.
            lastDeleted = null;
            deleteError = $"되돌리지 못했습니다. {ex.Message}";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "삭제한 지출을 되돌리지 못했습니다.");
            deleteError = "되돌리지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            undoBusy = false;
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
            lastDeleted = null;
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

}
