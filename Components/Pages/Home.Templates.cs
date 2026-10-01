using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Components.Pages;

public partial class Home
{
    private async Task LoadPaymentMethodsAsync()
    {
        await CheckOwnerAsync();
        var loaded = await MethodService.ListAsync(ownerId);
        await CheckOwnerAsync();
        paymentMethods = loaded;
    }

    private async Task RefreshPaymentMethodsAsync()
    {
        if (paymentBusy || isSavingEdit) return;
        paymentBusy = true;
        try { await LoadPaymentMethodsAsync(); await LoadExpensesAsync(); paymentError = null; }
        catch (Exception ex) { Logger.LogError(ex, "결제수단 조회 실패"); paymentError = "목록을 불러오지 못했습니다. 새로고침 후 확인해 주세요."; }
        finally { paymentBusy = false; }
    }

    private async Task RefreshTemplatesAsync()
    {
        if (templateBusy) return;
        templateBusy = true;
        try
        {
            await CheckOwnerAsync();
            var loaded = await TemplateService.ListAsync(ownerId);
            await CheckOwnerAsync();
            templates = loaded;
            selectedTemplateId = "";
            templateError = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "즐겨찾기 템플릿 목록을 불러오지 못했습니다.");
            templateError = "템플릿을 불러오지 못했습니다. 페이지를 새로고침해 주세요.";
        }
        finally { templateBusy = false; }
    }

    private async Task ApplyTemplateAsync(ChangeEventArgs args)
    {
        if (templateBusy) return;
        selectedTemplateId = args.Value?.ToString() ?? "";
        templateNotice = null;
        templateError = null;
        if (selectedTemplateId == "") return;
        if (!int.TryParse(selectedTemplateId, out var id)) return;
        templateBusy = true;
        try
        {
            await CheckOwnerAsync();
            var template = await TemplateService.FindAsync(ownerId, id);
            await CheckOwnerAsync();
            if (template is null)
            {
                selectedTemplateId = "";
                templateError = "템플릿이 삭제되었거나 사용할 수 없습니다. 목록을 새로고침해 주세요.";
                return;
            }
            await LoadPaymentMethodsAsync();
            await ExpenseService.ValidatePaymentAsync(ownerId, template.PaymentMethodId);
            amount = template.Amount;
            category = template.Category;
            memo = template.Memo;
            paymentMethodId = template.PaymentMethodId;
            templateNotice = $"‘{template.Name}’을 입력했습니다. 내용을 확인한 뒤 지출 기록하기를 눌러 주세요.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "즐겨찾기 템플릿 적용에 실패했습니다.");
            templateError = "템플릿을 적용하지 못했습니다. 페이지를 새로고침해 주세요.";
        }
        finally { templateBusy = false; }
    }
}
