namespace MyExpenses.Data;

public sealed class IncomeRecord
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    // 지출과 같이 정수 원 단위로 저장합니다.
    public long Amount { get; set; }

    public string Source { get; set; } = "급여";

    public string Memo { get; set; } = string.Empty;

    public static readonly IReadOnlyList<string> Sources = Array.AsReadOnly(new[] { "급여", "부수입", "용돈", "이자·투자", "기타" });
}

public sealed record MonthlyCashflow(long Income, long Expense)
{
    public long Net => Income - Expense;

    // 수입이 없으면 저축률을 계산할 수 없으므로 null입니다.
    public double? SavingsRate => Income > 0 ? Math.Round(Net * 100d / Income, 1) : null;
}
