using System.Globalization;

namespace MyExpenses.Data;

// UI와 CSV 요청이 같은 입력 검증 규칙을 사용합니다.
public sealed class ExpenseSearchInput
{
    public string Month { get; set; } = "";
    public string Category { get; set; } = "";
    public string Search { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public string MinAmount { get; set; } = "";
    public string MaxAmount { get; set; } = "";
    public string Sort { get; set; } = nameof(ExpenseSort.Newest);
    public string PaymentMethod { get; set; } = "";
    // 태그 번호(빈 문자열이면 전체)
    public string Tag { get; set; } = "";

    public bool TryCreate(out ExpenseFilter filter, out string? error)
    {
        filter = new();
        error = null;
        if (!TryDate(Month, "yyyy-MM", out var month) ||
            !TryDate(StartDate, "yyyy-MM-dd", out var start) ||
            !TryDate(EndDate, "yyyy-MM-dd", out var end))
            error = "월과 시작일·종료일을 올바르게 입력해 주세요.";
        else if (start.HasValue && end.HasValue && start > end)
            error = "시작일은 종료일보다 늦을 수 없습니다.";
        else if (!TryAmount(MinAmount, out var min) || !TryAmount(MaxAmount, out var max))
            error = "금액은 0 이상의 정수로 입력해 주세요. 입력 가능한 최대 금액은 9,223,372,036,854,775,807원입니다.";
        else if (min.HasValue && max.HasValue && min > max)
            error = "최소 금액은 최대 금액보다 클 수 없습니다.";
        else if (Search.Trim().Length > 100)
            error = "검색어는 100자 이하로 입력해 주세요.";
        else if (Category != "" && (string.IsNullOrWhiteSpace(Category) || Category.Length > 30))
            error = "카테고리를 다시 선택해 주세요.";
        else if (!Enum.TryParse<ExpenseSort>(Sort, out var sort) || !Enum.IsDefined(sort))
            error = "정렬 방법을 다시 선택해 주세요.";
        else if (PaymentMethod is not ("" or "none") && (!int.TryParse(PaymentMethod, NumberStyles.None, CultureInfo.InvariantCulture, out var methodId) || methodId <= 0))
            error = "결제수단을 다시 선택해 주세요.";
        else if (Tag != "" && (!int.TryParse(Tag, NumberStyles.None, CultureInfo.InvariantCulture, out var tagId) || tagId <= 0))
            error = "태그를 다시 선택해 주세요.";
        else
            filter = new(month, Category == "" ? null : Category, Search.Trim(), start, end, min, max, sort,
                PaymentMethod is "" or "none" ? null : int.Parse(PaymentMethod, CultureInfo.InvariantCulture), PaymentMethod == "none",
                Tag == "" ? null : int.Parse(Tag, CultureInfo.InvariantCulture));
        return error is null;
    }

    private static bool TryDate(string input, string format, out DateTime? result)
    {
        result = null;
        if (input == "") return true;
        if (!DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        result = date;
        return true;
    }

    private static bool TryAmount(string input, out long? result)
    {
        result = null;
        if (input == "") return true;
        if (!long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)) return false;
        result = amount;
        return true;
    }
}
