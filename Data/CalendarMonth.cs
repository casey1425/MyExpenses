using System.Globalization;

namespace MyExpenses.Data;

public sealed record CalendarDay(DateOnly Date, bool InMonth, bool IsToday, long Expense, int ExpenseCount, long Income, int IncomeCount)
{
    public bool HasRecords => ExpenseCount > 0 || IncomeCount > 0;
}

public sealed record CalendarMonthView(DateOnly Month, IReadOnlyList<IReadOnlyList<CalendarDay>> Weeks,
    long ExpenseTotal, int ExpenseCount, long IncomeTotal, int IncomeCount)
{
    public long Net => IncomeTotal - ExpenseTotal;
}

// 한 달의 날짜별 수입·지출 합계를 일요일부터 시작하는 주 단위 표로 만듭니다.
public static class CalendarMonth
{
    public static readonly DateOnly MinimumMonth = new(2, 1, 1);
    public static readonly DateOnly MaximumMonth = new(9998, 12, 1);

    public static CalendarMonthView Calculate(IEnumerable<ExpenseRecord> expenses, IEnumerable<IncomeRecord> incomes,
        DateOnly month, DateOnly today)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        if (month < MinimumMonth || month > MaximumMonth) throw new ArgumentOutOfRangeException(nameof(month));

        var last = month.AddMonths(1).AddDays(-1);
        var expenseByDay = expenses.Select(e => (Date: DateOnly.FromDateTime(e.Date), e.Amount)).Where(e => e.Date >= month && e.Date <= last)
            .GroupBy(e => e.Date).ToDictionary(g => g.Key, g => (Amount: g.Sum(e => e.Amount), Count: g.Count()));
        var incomeByDay = incomes.Select(i => (Date: DateOnly.FromDateTime(i.Date), i.Amount)).Where(i => i.Date >= month && i.Date <= last)
            .GroupBy(i => i.Date).ToDictionary(g => g.Key, g => (Amount: g.Sum(i => i.Amount), Count: g.Count()));

        // 일요일 시작: 1일이 속한 주의 일요일부터, 말일이 속한 주의 토요일까지.
        var start = month.AddDays(-(int)month.DayOfWeek);
        var end = last.AddDays(6 - (int)last.DayOfWeek);
        var weeks = new List<IReadOnlyList<CalendarDay>>();
        for (var weekStart = start; weekStart <= end; weekStart = weekStart.AddDays(7))
        {
            var days = new List<CalendarDay>(7);
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                var inMonth = date >= month && date <= last;
                var expense = inMonth ? expenseByDay.GetValueOrDefault(date) : default;
                var income = inMonth ? incomeByDay.GetValueOrDefault(date) : default;
                days.Add(new CalendarDay(date, inMonth, date == today, expense.Amount, expense.Count, income.Amount, income.Count));
            }
            weeks.Add(days);
        }

        return new CalendarMonthView(month, weeks, expenseByDay.Values.Sum(v => v.Amount), expenseByDay.Values.Sum(v => v.Count),
            incomeByDay.Values.Sum(v => v.Amount), incomeByDay.Values.Sum(v => v.Count));
    }

    // 좁은 칸에 들어가도록 금액을 줄여 씁니다. 1만 미만은 그대로, 그 이상은 만·억 단위입니다.
    public static string Compact(long amount)
    {
        if (amount <= 0) return string.Empty;
        if (amount < 10_000) return amount.ToString("N0", CultureInfo.InvariantCulture);
        if (amount < 100_000_000)
        {
            var man = amount / 10_000.0;
            return (man < 100 ? man.ToString("0.#", CultureInfo.InvariantCulture) : Math.Floor(man).ToString("0", CultureInfo.InvariantCulture)) + "만";
        }
        return (amount / 100_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "억";
    }
}
