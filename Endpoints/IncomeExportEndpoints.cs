using System.Globalization;
using System.Security.Claims;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses;

public static class IncomeExportEndpoints
{
    public static IEndpointRouteBuilder MapIncomeExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/export/income.csv", ExportAsync).RequireAuthorization();
        return endpoints;
    }

    // month=yyyy-MM을 주면 해당 월만, 없으면 전체 수입을 내보냅니다.
    private static async Task<IResult> ExportAsync(
        HttpContext context,
        IncomeService incomeService,
        CancellationToken cancellationToken)
    {
        var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var monthValue = context.Request.Query["month"].ToString();
        List<IncomeRecord> incomes;
        string scope;
        if (monthValue == "")
        {
            incomes = await incomeService.ListAllAsync(ownerId, cancellationToken);
            scope = "all";
        }
        else if (DateTime.TryParseExact(monthValue, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            incomes = await incomeService.ListAsync(ownerId, month, cancellationToken);
            scope = monthValue;
        }
        else
        {
            return Results.BadRequest("내보낼 월을 yyyy-MM 형식으로 입력해 주세요.");
        }

        var fileName = $"MyExpenses-income-{scope}-{KoreanClock.Today:yyyy-MM-dd}.csv";
        return Results.File(IncomeCsvExporter.Create(incomes), "text/csv; charset=utf-8", fileName);
    }
}
