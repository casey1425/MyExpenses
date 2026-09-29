namespace MyExpenses.Data;

public sealed class CategoryBudget
{
    public string OwnerId { get; set; } = string.Empty;
    public DateTime Month { get; set; }
    public string Category { get; set; } = string.Empty;
    public long Amount { get; set; }
}
