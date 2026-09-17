namespace MyExpenses.Data;

public sealed record ExpenseFilter(DateTime? Month = null, string? Category = null)
{
    public bool IsActive => Month.HasValue || !string.IsNullOrEmpty(Category);

    public IQueryable<ExpenseRecord> ApplyTo(IQueryable<ExpenseRecord> query)
    {
        if (Month is DateTime month)
        {
            var start = new DateTime(month.Year, month.Month, 1);
            if (start.Year == 9999 && start.Month == 12)
            {
                query = query.Where(expense => expense.Date >= start);
            }
            else
            {
                var end = start.AddMonths(1);
                query = query.Where(expense => expense.Date >= start && expense.Date < end);
            }
        }

        if (!string.IsNullOrEmpty(Category))
        {
            var category = Category;
            query = query.Where(expense => expense.Category == category);
        }

        return query;
    }

    public bool Matches(ExpenseRecord expense) =>
        (!Month.HasValue || (expense.Date.Year == Month.Value.Year && expense.Date.Month == Month.Value.Month)) &&
        (string.IsNullOrEmpty(Category) || expense.Category == Category);
}
