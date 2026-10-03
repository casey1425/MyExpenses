namespace MyExpenses.Data;

public sealed class RecurringIncomeRule
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public DateTime StartMonth { get; set; }
    public int DayOfMonth { get; set; }
    public long Amount { get; set; }
    public string Source { get; set; } = "급여";
    public string Memo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class RecurringIncomeOccurrence
{
    public int RuleId { get; set; }
    public DateTime Month { get; set; }
    public string OwnerId { get; set; } = string.Empty;
}
