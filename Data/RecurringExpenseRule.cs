namespace MyExpenses.Data;

public sealed class RecurringExpenseRule
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public DateTime StartMonth { get; set; }
    public int DayOfMonth { get; set; }
    public long Amount { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Memo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class RecurringExpenseOccurrence
{
    public int RuleId { get; set; }
    public DateTime Month { get; set; }
    public string OwnerId { get; set; } = string.Empty;
}
