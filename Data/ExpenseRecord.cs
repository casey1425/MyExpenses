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
    public int? PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public List<ExpenseTag> TagLinks { get; set; } = [];

    // 태그 이름을 가나다·알파벳 순으로 돌려줍니다. TagLinks와 Tag를 함께 읽었을 때만 값이 있습니다.
    public IReadOnlyList<string> TagNames => TagLinks.Where(link => link.Tag is not null)
        .Select(link => link.Tag!.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
}
