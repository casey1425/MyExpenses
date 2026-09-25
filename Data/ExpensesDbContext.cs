using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<ExpenseRecord> Expenses => Set<ExpenseRecord>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExpenseRecord>(entity =>
        {
            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.Category).IsRequired().HasMaxLength(30);
            entity.Property(expense => expense.Memo).IsRequired().HasMaxLength(100);
            entity.Property(expense => expense.OwnerId).IsRequired();
            entity.HasIndex(expense => new { expense.OwnerId, expense.Date });
        });

        modelBuilder.Entity<MonthlyBudget>(entity =>
        {
            entity.HasKey(budget => new { budget.OwnerId, budget.Month });
            entity.Property(budget => budget.OwnerId).IsRequired();
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(profile => profile.OwnerId);
            entity.Property(profile => profile.OwnerId).IsRequired();
        });
    }
}
