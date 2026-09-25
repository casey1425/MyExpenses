using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

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
        await db.MonthlyBudgets
            .Where(budget => budget.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.UserProfiles
            .Where(profile => profile.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
