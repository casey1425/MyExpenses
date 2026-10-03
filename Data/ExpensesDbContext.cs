using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public sealed class ExpensesDbContext(DbContextOptions<ExpensesDbContext> options) : DbContext(options)
{
    public DbSet<ExpenseRecord> Expenses => Set<ExpenseRecord>();
    public DbSet<IncomeRecord> Incomes => Set<IncomeRecord>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<SavingsDeposit> SavingsDeposits => Set<SavingsDeposit>();
    public DbSet<UserCategory> UserCategories => Set<UserCategory>();
    public DbSet<ExpenseTemplate> ExpenseTemplates => Set<ExpenseTemplate>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RecurringExpenseRule> RecurringExpenseRules => Set<RecurringExpenseRule>();
    public DbSet<RecurringExpenseOccurrence> RecurringExpenseOccurrences => Set<RecurringExpenseOccurrence>();
    public DbSet<RecurringIncomeRule> RecurringIncomeRules => Set<RecurringIncomeRule>();
    public DbSet<RecurringIncomeOccurrence> RecurringIncomeOccurrences => Set<RecurringIncomeOccurrence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserCategory>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.OwnerId).IsRequired();
            entity.Property(c => c.Name).IsRequired().HasMaxLength(30);
            entity.HasIndex(c => new { c.OwnerId, c.Name }).IsUnique();
        });
        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasAlternateKey(m => new { m.OwnerId, m.Id });
            entity.Property(m => m.OwnerId).IsRequired();
            entity.Property(m => m.Name).IsRequired().HasMaxLength(50);
            entity.Property(m => m.Type).IsRequired().HasMaxLength(20);
            entity.HasIndex(m => new { m.OwnerId, m.Name }).IsUnique();
        });
        modelBuilder.Entity<ExpenseTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.OwnerId).IsRequired();
            entity.Property(t => t.Name).IsRequired().HasMaxLength(50);
            entity.Property(t => t.Category).IsRequired().HasMaxLength(30);
            entity.Property(t => t.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(t => t.OwnerId);
            entity.HasOne(t => t.PaymentMethod).WithMany().HasForeignKey(t => new { t.OwnerId, t.PaymentMethodId })
                .HasPrincipalKey(m => new { m.OwnerId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ExpenseRecord>(entity =>
        {
            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.Category).IsRequired().HasMaxLength(30);
            entity.Property(expense => expense.Memo).IsRequired().HasMaxLength(100);
            entity.Property(expense => expense.OwnerId).IsRequired();
            entity.HasIndex(expense => new { expense.OwnerId, expense.Date });
            entity.HasOne(e => e.PaymentMethod).WithMany().HasForeignKey(e => new { e.OwnerId, e.PaymentMethodId })
                .HasPrincipalKey(m => new { m.OwnerId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IncomeRecord>(entity =>
        {
            entity.HasKey(income => income.Id);
            entity.Property(income => income.OwnerId).IsRequired();
            entity.Property(income => income.Source).IsRequired().HasMaxLength(20);
            entity.Property(income => income.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(income => new { income.OwnerId, income.Date });
        });

        modelBuilder.Entity<SavingsGoal>(entity =>
        {
            entity.HasKey(goal => goal.Id);
            entity.Property(goal => goal.OwnerId).IsRequired();
            entity.Property(goal => goal.Name).IsRequired().HasMaxLength(50);
            entity.HasIndex(goal => new { goal.OwnerId, goal.Name }).IsUnique();
        });

        modelBuilder.Entity<SavingsDeposit>(entity =>
        {
            entity.HasKey(deposit => deposit.Id);
            entity.Property(deposit => deposit.OwnerId).IsRequired();
            entity.Property(deposit => deposit.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(deposit => new { deposit.OwnerId, deposit.GoalId });
            entity.HasOne<SavingsGoal>().WithMany().HasForeignKey(deposit => deposit.GoalId).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<RecurringIncomeRule>(entity =>
        {
            entity.HasKey(rule => rule.Id);
            entity.Property(rule => rule.OwnerId).IsRequired();
            entity.Property(rule => rule.Source).IsRequired().HasMaxLength(20);
            entity.Property(rule => rule.Memo).IsRequired().HasMaxLength(100);
            entity.HasIndex(rule => rule.OwnerId);
        });

        modelBuilder.Entity<RecurringIncomeOccurrence>(entity =>
        {
            entity.HasKey(occurrence => new { occurrence.RuleId, occurrence.Month });
            entity.Property(occurrence => occurrence.OwnerId).IsRequired();
            entity.HasIndex(occurrence => occurrence.OwnerId);
        });
    }
}
