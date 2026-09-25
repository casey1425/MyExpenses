namespace MyExpenses.Data;

public sealed class ExpenseRecord
{
    public int Id { get; set; }

    public string OwnerId { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    // 원화는 소수점이 필요하지 않으므로 정수 원 단위로 저장합니다.
    public long Amount { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Memo { get; set; } = string.Empty;
}
