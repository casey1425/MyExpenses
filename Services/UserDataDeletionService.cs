using Microsoft.EntityFrameworkCore;

using MyExpenses.Data;

namespace MyExpenses.Services;

public sealed class UserDataDeletionService(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    public async Task DeleteAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Expenses
            .Where(expense => expense.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.Incomes.Where(income => income.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        await db.MonthlyBudgets
            .Where(budget => budget.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.CategoryBudgets
            .Where(budget => budget.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.RecurringExpenseOccurrences
            .Where(occurrence => occurrence.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.RecurringExpenseRules
            .Where(rule => rule.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.UserProfiles
            .Where(profile => profile.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ExpenseTemplates.Where(template => template.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.PaymentMethods.Where(method => method.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);
        await db.UserCategories.Where(category => category.OwnerId == ownerId).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
