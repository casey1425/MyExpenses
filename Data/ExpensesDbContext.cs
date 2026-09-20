using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<ExpenseRecord> Expenses => Set<ExpenseRecord>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExpenseRecord>(entity =>
        {
            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.Category).IsRequired().HasMaxLength(30);
            entity.Property(expense => expense.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(expense => expense.Date);
        });

        modelBuilder.Entity<MonthlyBudget>(entity =>
        {
            entity.HasKey(budget => budget.Month);
        });
    }
}
