using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyExpenses.Components.Pages;
using MyExpenses.Data;
using MyExpenses.Services;

namespace MyExpenses.Testing;

public sealed class TestFactory(DbContextOptions<ExpensesDbContext> options) : IDbContextFactory<ExpensesDbContext>
{
    public ExpensesDbContext CreateDbContext() => new(options);
    public Task<ExpensesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}

public sealed class TestAuth(string ownerId = "A") : AuthenticationStateProvider
{
    public string OwnerId { get; set; } = ownerId;
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, OwnerId) }, "test"))));
}

public static class TestComponents
{
    public static Home CreateHome(TestFactory factory, TestAuth? authentication = null)
    {
        var home = new Home();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        void Inject(string name, object value) => typeof(Home).GetProperty(name, flags)!.SetValue(home, value);
        var auth = authentication ?? new TestAuth();
        Inject("AuthenticationStateProvider", auth);
        Inject("Logger", NullLogger<Home>.Instance);
        Inject("TemplateService", new ExpenseTemplateService(factory));
        Inject("MethodService", new PaymentMethodService(factory));
        Inject("RecurringIncomeService", new RecurringIncomeService(factory));
        Inject("ExpenseService", new ExpenseService(factory));
        Inject("BudgetService", new BudgetService(factory));
        Inject("CategoryService", new CategoryService(factory));
        Inject("TagService", new TagService(factory));
        typeof(Home).GetField("ownerId", flags)!.SetValue(home, auth.OwnerId);
        return home;
    }
}
