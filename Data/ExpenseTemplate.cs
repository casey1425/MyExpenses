using System.Globalization;

namespace MyExpenses.Data;

public sealed class ExpenseTemplate
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public long Amount { get; set; }
    public string Category { get; set; } = "";
    public string Memo { get; set; } = "";
    public int? PaymentMethodId { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
}

public sealed class ExpenseTemplateInput
{
    public static IReadOnlyList<string> Categories => ExpenseCategories.All;
    public string Name { get; set; } = "";
    public string Amount { get; set; } = "";
    public string Category { get; set; } = "식비";
    public string Memo { get; set; } = "";
    public int? PaymentMethodId { get; set; }

    public ExpenseTemplate Validate(IReadOnlyList<string>? categories = null)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 50)
            throw new ArgumentException("템플릿 이름은 1~50자로 입력해 주세요.");
        if (!long.TryParse(Amount, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            throw new ArgumentException("금액은 1원 이상의 정수로 입력해 주세요. 최대 금액은 9,223,372,036,854,775,807원입니다.");
        if (!(categories ?? Categories).Contains(Category) || Memo.Trim().Length > 100)
            throw new ArgumentException("카테고리와 100자 이하 메모를 확인해 주세요.");
        return new ExpenseTemplate { Name = Name.Trim(), Amount = amount, Category = Category, Memo = Memo.Trim(), PaymentMethodId = PaymentMethodId };
    }
}
