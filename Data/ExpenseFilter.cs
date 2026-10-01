namespace MyExpenses.Data;

public enum ExpenseSort { Newest, Oldest, HighestAmount, LowestAmount }

public sealed record ExpenseFilter(DateTime? Month = null, string? Category = null,
    string? Search = null, DateTime? StartDate = null, DateTime? EndDate = null,
    long? MinAmount = null, long? MaxAmount = null, ExpenseSort Sort = ExpenseSort.Newest,
    int? PaymentMethodId = null, bool UnspecifiedPayment = false)
{
    public bool IsActive => Month.HasValue || !string.IsNullOrEmpty(Category) ||
        !string.IsNullOrWhiteSpace(Search) || StartDate.HasValue || EndDate.HasValue ||
        MinAmount.HasValue || MaxAmount.HasValue || PaymentMethodId.HasValue || UnspecifiedPayment;

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

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var keyword = Search.Trim();
            query = query.Where(expense => expense.Memo.Contains(keyword));
        }
        if (StartDate is DateTime startDate)
            query = query.Where(expense => expense.Date >= startDate.Date);
        if (EndDate is DateTime endDate)
            query = query.Where(expense => expense.Date.Date <= endDate.Date);
        if (MinAmount is long min)
            query = query.Where(expense => expense.Amount >= min);
        if (MaxAmount is long max)
            query = query.Where(expense => expense.Amount <= max);
        if (PaymentMethodId is int methodId)
            query = query.Where(expense => expense.PaymentMethodId == methodId);
        if (UnspecifiedPayment)
            query = query.Where(expense => expense.PaymentMethodId == null);
        return query;
    }

    public IOrderedQueryable<ExpenseRecord> Order(IQueryable<ExpenseRecord> query) => Sort switch
    {
        ExpenseSort.Oldest => query.OrderBy(e => e.Date).ThenBy(e => e.Id),
        ExpenseSort.HighestAmount => query.OrderByDescending(e => e.Amount).ThenByDescending(e => e.Date).ThenByDescending(e => e.Id),
        ExpenseSort.LowestAmount => query.OrderBy(e => e.Amount).ThenByDescending(e => e.Date).ThenByDescending(e => e.Id),
        _ => query.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
    };

    public bool Matches(ExpenseRecord expense) =>
        (!Month.HasValue || (expense.Date.Year == Month.Value.Year && expense.Date.Month == Month.Value.Month)) &&
        (string.IsNullOrEmpty(Category) || expense.Category == Category) &&
        (string.IsNullOrWhiteSpace(Search) || expense.Memo.Contains(Search.Trim(), StringComparison.Ordinal)) &&
        (!StartDate.HasValue || expense.Date >= StartDate.Value.Date) &&
        (!EndDate.HasValue || expense.Date.Date <= EndDate.Value.Date) &&
        (!MinAmount.HasValue || expense.Amount >= MinAmount) &&
        (!MaxAmount.HasValue || expense.Amount <= MaxAmount) &&
        (!PaymentMethodId.HasValue || expense.PaymentMethodId == PaymentMethodId) &&
        (!UnspecifiedPayment || expense.PaymentMethodId == null);
}
