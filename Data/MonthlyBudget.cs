namespace MyExpenses.Data;

public sealed class MonthlyBudget
{
    public string OwnerId { get; set; } = string.Empty;
    public DateTime Month { get; set; }
    public long Amount { get; set; }
}
