using System.Globalization;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyExpenses;
using MyExpenses.Components;
using MyExpenses.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

var authentication = builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    });
authentication.AddIdentityCookies();

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.Events.OnRemoteFailure = context =>
        {
            var message = Uri.EscapeDataString("Google 로그인이 취소되었거나 실패했습니다.");
            context.Response.Redirect($"/account/login?error={message}");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "myexpenses.db");

builder.Services.AddDbContextFactory<ExpensesDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

var authDatabasePath = Path.Combine(dataDirectory, "auth.db");
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlite($"Data Source={authDatabasePath}"));
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/account/login";
    options.AccessDeniedPath = "/account/login";
});

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ExpensesDbContext>>()
    .CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
    await BudgetSchema.EnsureCreatedAsync(db);
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await authDb.Database.EnsureCreatedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAccountEndpoints();
app.MapGet("/export/expenses.csv", ExportExpensesAsync).RequireAuthorization();

app.Run();

static async Task<IResult> ExportExpensesAsync(
    HttpContext context,
    IDbContextFactory<ExpensesDbContext> dbFactory,
    CancellationToken cancellationToken)
{
    var scope = context.Request.Query["scope"].ToString();
    if (scope is not ("all" or "filtered"))
        return Results.BadRequest("내보내기 범위를 다시 선택해 주세요.");

    DateTime? month = null;
    string? category = null;
    if (scope == "filtered")
    {
        var monthValue = context.Request.Query["month"].ToString();
        if (!string.IsNullOrEmpty(monthValue))
        {
            if (!DateTime.TryParseExact(monthValue, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedMonth))
                return Results.BadRequest("조회할 월을 다시 선택해 주세요.");

            month = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
        }

        category = context.Request.Query["category"].ToString();
        if (string.IsNullOrEmpty(category))
            category = null;
    }

    await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
    var expenses = await new ExpenseFilter(month, category)
        .ApplyTo(db.Expenses.AsNoTracking())
        .OrderByDescending(expense => expense.Date)
        .ThenByDescending(expense => expense.Id)
        .ToListAsync(cancellationToken);

    var fileName = $"MyExpenses-{scope}-{DateTime.Today:yyyy-MM-dd}.csv";
    return Results.File(ExpenseCsvExporter.Create(expenses), "text/csv; charset=utf-8", fileName);
}
