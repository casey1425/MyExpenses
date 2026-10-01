using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Data;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new ExpensesDbContext(new DbContextOptionsBuilder<ExpensesDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var rows = new[]
{
    new ExpenseRecord { OwnerId = "A", Date = new(2026, 9, 30, 23, 59, 59), Amount = 5000, Category = "카페", Memo = "커피 100%_" },
    new ExpenseRecord { OwnerId = "A", Date = new(2026, 10, 1), Amount = 10000, Category = "카페", Memo = "커피" },
    new ExpenseRecord { OwnerId = "A", Date = new(2026, 9, 30), Amount = 1000, Category = "교통", Memo = "택시" },
    new ExpenseRecord { OwnerId = "B", Date = new(2026, 9, 30), Amount = 5000, Category = "카페", Memo = "커피 100%_" },
    new ExpenseRecord { OwnerId = "A", Date = new(2026, 9, 1), Amount = 5000, Category = "카페", Memo = "Coffee" },
    new ExpenseRecord { OwnerId = "A", Date = new(9999, 12, 31, 23, 59, 59), Amount = 1, Category = "기타", Memo = "끝" }
};
db.Expenses.AddRange(rows);
await db.SaveChangesAsync();
var owned = db.Expenses.AsNoTracking().Where(e => e.OwnerId == "A");
var input = new ExpenseSearchInput { Month = "2026-09", Category = "카페", Search = "  커피  ", StartDate = "2026-09-30", EndDate = "2026-09-30", MinAmount = "5000", MaxAmount = "5000" };
Check(input.TryCreate(out var filter, out _), "Valid input rejected");
var matched = await filter.Order(filter.ApplyTo(owned)).ToListAsync();
Check(matched.Count == 1 && matched[0].Id == rows[0].Id, "Combined filters, inclusive endpoints or isolation failed");
Check(rows.Where(e => e.OwnerId == "A").All(e => filter.Matches(e) == matched.Any(m => m.Id == e.Id)), "Matches differs from SQL");
Check((await new ExpenseFilter(Search: "%_").ApplyTo(owned).ToListAsync()).Count == 1, "Wildcard must be literal");
Check((await new ExpenseFilter(Search: "coffee").ApplyTo(owned).ToListAsync()).Count == 0, "Search case sensitivity differs");
Check((await new ExpenseFilter(Month: new(9999, 12, 1), EndDate: DateTime.MaxValue.Date).ApplyTo(owned).ToListAsync()).Count == 1, "Max date boundary failed");
Check((await new ExpenseFilter(StartDate: new(2026, 10, 1)).ApplyTo(owned).ToListAsync()).Count == 2, "Open date range failed");
Check(!new ExpenseFilter(Sort: ExpenseSort.Oldest).IsActive, "Sorting must not restrict records");
foreach (var sort in Enum.GetValues<ExpenseSort>())
{
    var sorting = new ExpenseFilter(Sort: sort);
    var actual = (await sorting.Order(owned).ToListAsync()).Select(e => e.Id);
    var expected = sorting.Order(rows.Where(e => e.OwnerId == "A").AsQueryable()).Select(e => e.Id);
    Check(actual.SequenceEqual(expected), $"SQL sort failed: {sort}");
}
var invalid = new[]
{
    new ExpenseSearchInput { StartDate = "2026-02-30" },
    new ExpenseSearchInput { Month = "bad" },
    new ExpenseSearchInput { StartDate = "2026-10-02", EndDate = "2026-10-01" },
    new ExpenseSearchInput { MinAmount = "5", MaxAmount = "4" },
    new ExpenseSearchInput { MinAmount = "-1" },
    new ExpenseSearchInput { MinAmount = "1.5" },
    new ExpenseSearchInput { MaxAmount = "9223372036854775808" },
    new ExpenseSearchInput { Search = new string('a', 101) },
    new ExpenseSearchInput { Category = "invalid" },
    new ExpenseSearchInput { Sort = "999" }
};
foreach (var bad in invalid) Check(!bad.TryCreate(out _, out var error) && error is not null, "Invalid input accepted");
Check(new ExpenseSearchInput { MinAmount = "0", MaxAmount = long.MaxValue.ToString() }.TryCreate(out _, out _), "Valid amount boundaries rejected");
Check(new ExpenseSearchInput().TryCreate(out var reset, out _) && !reset.IsActive && reset.Sort == ExpenseSort.Newest, "Reset defaults failed");
Check((await reset.ApplyTo(owned).CountAsync()) == 5, "Reset or owner isolation failed");
var csv = System.Text.Encoding.UTF8.GetString(ExpenseCsvExporter.Create(matched));
Check(csv.Contains("커피 100%_") && !csv.Contains("택시"), "Filtered CSV failed");
var largeStatistics = ExpenseStatistics.ByCategory(new[]
{
    new ExpenseRecord { Amount = long.MaxValue, Category = "기타" },
    new ExpenseRecord { Amount = long.MaxValue, Category = "기타" }
});
Check(largeStatistics.Single().Amount == (decimal)long.MaxValue * 2, "Aggregate overflow");
Console.WriteLine("PASS: SQLite combined filters, inclusive boundaries, literal search, four sorts, owner isolation, input validation, reset and CSV");
