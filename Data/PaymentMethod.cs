namespace MyExpenses.Data;

public sealed class PaymentMethod
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "현금";
    public static readonly IReadOnlyList<string> Types = Array.AsReadOnly(new[] { "현금", "체크카드", "신용카드", "계좌이체", "기타" });
}

public sealed record PaymentMethodTotal(int? Id, string Name, string Type, decimal Amount, int Count);
