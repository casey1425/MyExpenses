using Microsoft.EntityFrameworkCore;

namespace MyExpenses.Data;

public sealed class UserDataProvisioner(IDbContextFactory<ExpensesDbContext> dbFactory)
{
    private static readonly SemaphoreSlim ProvisioningLock = new(1, 1);

    public async Task<bool> EnsureUserAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await ProvisioningLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var profile = await db.UserProfiles.FindAsync([ownerId], cancellationToken);
            if (profile is not null)
                return !profile.HasCompletedOnboarding;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var hasLegacyExpenses = await db.Expenses.AnyAsync(expense => expense.OwnerId == string.Empty,
                cancellationToken);
            var hasLegacyBudgets = await db.MonthlyBudgets.AnyAsync(budget => budget.OwnerId == string.Empty,
                cancellationToken);
            var claimedLegacyData = hasLegacyExpenses || hasLegacyBudgets;

            if (hasLegacyExpenses)
            {
                await db.Expenses.Where(expense => expense.OwnerId == string.Empty)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(expense => expense.OwnerId, ownerId),
                        cancellationToken);
            }

            if (hasLegacyBudgets)
            {
                var legacyBudgets = await db.MonthlyBudgets
                    .Where(budget => budget.OwnerId == string.Empty)
                    .ToListAsync(cancellationToken);
                foreach (var budget in legacyBudgets)
                {
                    db.MonthlyBudgets.Remove(budget);
                    db.MonthlyBudgets.Add(new MonthlyBudget
                    {
                        OwnerId = ownerId,
                        Month = budget.Month,
                        Amount = budget.Amount
                    });
                }
            }

            db.UserProfiles.Add(new UserProfile
            {
                OwnerId = ownerId,
                HasCompletedOnboarding = claimedLegacyData
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return !claimedLegacyData;
        }
        finally
        {
            ProvisioningLock.Release();
        }
    }

    public async Task CompleteOnboardingAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.UserProfiles.FindAsync([ownerId], cancellationToken);
        if (profile is null)
        {
            db.UserProfiles.Add(new UserProfile { OwnerId = ownerId, HasCompletedOnboarding = true });
        }
        else
        {
            profile.HasCompletedOnboarding = true;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> NeedsOnboardingAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.UserProfiles
            .Where(profile => profile.OwnerId == ownerId)
            .Select(profile => !profile.HasCompletedOnboarding)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
