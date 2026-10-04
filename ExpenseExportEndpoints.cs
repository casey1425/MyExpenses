using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses;

public static class ExpenseExportEndpoints
{
    public static IEndpointRouteBuilder MapExpenseExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/export/expenses.csv", ExportAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ExportAsync(
        HttpContext context,
        ExpenseService expenseService,
        CancellationToken cancellationToken)
    {
        var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var scope = context.Request.Query["scope"].ToString();
        if (scope is not ("all" or "filtered"))
            return Results.BadRequest("내보내기 범위를 다시 선택해 주세요.");

        var filter = new ExpenseFilter();
        if (scope == "filtered")
        {
            var input = new ExpenseSearchInput
            {
                Month = context.Request.Query["month"].ToString(),
                Category = context.Request.Query["category"].ToString(),
                Search = context.Request.Query["search"].ToString(),
                StartDate = context.Request.Query["start"].ToString(),
                EndDate = context.Request.Query["end"].ToString(),
                MinAmount = context.Request.Query["min"].ToString(),
                MaxAmount = context.Request.Query["max"].ToString(),
                Sort = context.Request.Query["sort"].FirstOrDefault() ?? nameof(ExpenseSort.Newest),
                PaymentMethod = context.Request.Query["payment"].ToString()
            };
            if (!input.TryCreate(out filter, out var error))
                return Results.BadRequest(error);
        }

        List<ExpenseRecord> expenses;
        try
        {
            await expenseService.ValidatePaymentAsync(ownerId, filter.PaymentMethodId, cancellationToken);
            expenses = await expenseService.ListAsync(ownerId, filter, cancellationToken);
        }
        catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }

        var fileName = $"MyExpenses-{scope}-{KoreanClock.Today:yyyy-MM-dd}.csv";
        return Results.File(ExpenseCsvExporter.Create(expenses), "text/csv; charset=utf-8", fileName);
    }
}
