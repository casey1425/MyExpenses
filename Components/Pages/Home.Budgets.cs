using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Home
{
    private async Task LoadBudgetAsync()
    {
        await CheckOwnerAsync();
        var snapshot = await BudgetService.LoadAsync(ownerId, budgetMonth);
        await CheckOwnerAsync();
        budgetSpent = snapshot.Spent;
        categoryBudgetSpent = snapshot.CategorySpent;
        budgetAmount = snapshot.Amount;
        budgetInput = budgetAmount ?? 0;
        categoryBudgetAmounts = snapshot.CategoryAmounts;
        foreach (var option in categories)
            categoryBudgetInputs[option] = categoryBudgetAmounts.GetValueOrDefault(option);
    }

    private async Task RefreshBudgetAsync()
    {
        try
        {
            await LoadBudgetAsync();
            budgetError = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "월별 예산을 불러오지 못했습니다.");
            budgetError = "예산을 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private async Task OnBudgetMonthChangedAsync(ChangeEventArgs args)
    {
        var value = args.Value?.ToString() ?? string.Empty;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedMonth))
        {
            budgetError = "예산을 조회할 월을 다시 선택해 주세요.";
            return;
        }

        budgetMonth = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
        budgetMonthInput = value;
        budgetNotice = null;
        categoryBudgetNotice = null;
        categoryBudgetError = null;
        await RefreshBudgetAsync();
    }

    private async Task SaveBudgetAsync()
    {
        if (isSavingBudget)
            return;
        if (budgetInput <= 0)
        {
            budgetError = "예산은 1원 이상의 정수로 입력해 주세요.";
            return;
        }

        isSavingBudget = true;
        try
        {
            await CheckOwnerAsync();
            await BudgetService.SaveAsync(ownerId, budgetMonth, budgetInput);
            await LoadBudgetAsync();
            budgetError = null;
            budgetNotice = "예산을 저장했습니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "월별 예산을 저장하지 못했습니다.");
            budgetError = "예산 저장 결과를 확인하지 못했습니다. 새로고침 후 다시 확인해 주세요.";
        }
        finally
        {
            isSavingBudget = false;
        }
    }

    private async Task DeleteBudgetAsync()
    {
        if (isSavingBudget || budgetAmount is null)
            return;

        isSavingBudget = true;
        try
        {
            await CheckOwnerAsync();
            await BudgetService.DeleteAsync(ownerId, budgetMonth);
            await LoadBudgetAsync();
            budgetError = null;
            budgetNotice = "예산을 삭제했습니다. 지출 기록은 그대로 유지됩니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "월별 예산을 삭제하지 못했습니다.");
            budgetError = "예산 삭제 결과를 확인하지 못했습니다. 새로고침 후 다시 확인해 주세요.";
        }
        finally
        {
            isSavingBudget = false;
        }
    }

    private async Task SaveCategoryBudgetAsync(string selectedCategory)
    {
        if (savingCategoryBudget is not null || !categories.Contains(selectedCategory))
            return;
        var input = categoryBudgetInputs[selectedCategory];
        if (input <= 0)
        {
            categoryBudgetError = "카테고리 예산은 1원 이상의 정수로 입력해 주세요.";
            return;
        }

        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
        {
            categoryBudgetError = "로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.";
            return;
        }

        savingCategoryBudget = selectedCategory;
        try
        {
            await CheckOwnerAsync();
            await BudgetService.SaveAsync(ownerId, budgetMonth, input, selectedCategory);
            await LoadBudgetAsync();
            categoryBudgetError = null;
            categoryBudgetNotice = $"{selectedCategory} 예산을 저장했습니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "카테고리 예산을 저장하지 못했습니다.");
            categoryBudgetError = "카테고리 예산 저장 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
        }
        finally
        {
            savingCategoryBudget = null;
        }
    }

    private async Task DeleteCategoryBudgetAsync(string selectedCategory)
    {
        if (savingCategoryBudget is not null || !categories.Contains(selectedCategory) ||
            CategoryBudgetAmount(selectedCategory) is null)
            return;

        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        if (state.User.FindFirstValue(ClaimTypes.NameIdentifier) != ownerId)
        {
            categoryBudgetError = "로그인 계정이 바뀌었습니다. 페이지를 새로고침해 주세요.";
            return;
        }

        savingCategoryBudget = selectedCategory;
        try
        {
            await CheckOwnerAsync();
            await BudgetService.DeleteAsync(ownerId, budgetMonth, selectedCategory);
            await LoadBudgetAsync();
            categoryBudgetError = null;
            categoryBudgetNotice = $"{selectedCategory} 예산을 삭제했습니다. 지출 기록은 그대로 유지됩니다.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "카테고리 예산을 삭제하지 못했습니다.");
            categoryBudgetError = "카테고리 예산 삭제 결과를 확인하지 못했습니다. 새로고침 후 확인해 주세요.";
        }
        finally
        {
            savingCategoryBudget = null;
        }
    }
}
