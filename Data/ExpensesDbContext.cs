using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<ExpenseRecord> Expenses => Set<ExpenseRecord>();
    public DbSet<ExpenseTemplate> ExpenseTemplates => Set<ExpenseTemplate>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RecurringExpenseRule> RecurringExpenseRules => Set<RecurringExpenseRule>();
    public DbSet<RecurringExpenseOccurrence> RecurringExpenseOccurrences => Set<RecurringExpenseOccurrence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExpenseTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.OwnerId).IsRequired();
            entity.Property(t => t.Name).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Category).IsRequired().HasMaxLength(30);
            entity.Property(t => t.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(t => t.OwnerId);
        });
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

        modelBuilder.Entity<CategoryBudget>(entity =>
        {
            entity.HasKey(budget => new { budget.OwnerId, budget.Month, budget.Category });
            entity.Property(budget => budget.OwnerId).IsRequired();
            entity.Property(budget => budget.Category).IsRequired().HasMaxLength(30);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(profile => profile.OwnerId);
            entity.Property(profile => profile.OwnerId).IsRequired();
        });

        modelBuilder.Entity<RecurringExpenseRule>(entity =>
        {
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.OwnerId).IsRequired();
            entity.Property(rule => rule.Category).IsRequired().HasMaxLength(30);
            entity.Property(rule => rule.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(rule => rule.OwnerId);
        });

        modelBuilder.Entity<RecurringExpenseOccurrence>(entity =>
        {
            entity.HasKey(occurrence => new { occurrence.RuleId, occurrence.Month });
            entity.Property(occurrence => occurrence.OwnerId).IsRequired();
            entity.HasIndex(occurrence => occurrence.OwnerId);
        });
    }
}
