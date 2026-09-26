using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyExpenses;
using MyExpenses.Components;
using MyExpenses.Data;

var builder = WebApplication.CreateBuilder(args);

var configuredDataDirectory = builder.Configuration["Storage:DataDirectory"];
var dataDirectory = string.IsNullOrWhiteSpace(configuredDataDirectory)
    ? Path.Combine(builder.Environment.ContentRootPath, "Data")
    : Path.GetFullPath(configuredDataDirectory, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);

var keysDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .SetApplicationName("MyExpenses")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

var useForwardedHeaders = builder.Configuration.GetValue<bool>("ReverseProxy:UseForwardedHeaders");
if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

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

var databasePath = Path.Combine(dataDirectory, "myexpenses.db");

builder.Services.AddDbContextFactory<ExpensesDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<UserDataProvisioner>();
builder.Services.AddScoped<UserDataDeletionService>();
builder.Services.AddScoped<ExpenseCsvImportService>();

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
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
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

    var existingUserIds = await authDb.Users.AsNoTracking()
        .OrderBy(user => user.Id)
        .Select(user => user.Id)
        .Take(2)
        .ToListAsync();
    if (existingUserIds.Count == 1)
    {
        var provisioner = scope.ServiceProvider.GetRequiredService<UserDataProvisioner>();
        await provisioner.EnsureUserAsync(existingUserIds[0]);
    }
}

if (useForwardedHeaders)
    app.UseForwardedHeaders();

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
app.MapGet("/healthz", () => Results.Text("Healthy", "text/plain")).AllowAnonymous();
app.MapGet("/export/expenses.csv", ExportExpensesAsync).RequireAuthorization();

app.Run();

static async Task<IResult> ExportExpensesAsync(
    HttpContext context,
    IDbContextFactory<ExpensesDbContext> dbFactory,
    CancellationToken cancellationToken)
{
    var ownerId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrEmpty(ownerId))
        return Results.Unauthorized();

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
    var ownedExpenses = db.Expenses.AsNoTracking()
        .Where(expense => expense.OwnerId == ownerId);
    var expenses = await new ExpenseFilter(month, category)
        .ApplyTo(ownedExpenses)
        .OrderByDescending(expense => expense.Date)
        .ThenByDescending(expense => expense.Id)
        .ToListAsync(cancellationToken);

    var fileName = $"MyExpenses-{scope}-{DateTime.Today:yyyy-MM-dd}.csv";
    return Results.File(ExpenseCsvExporter.Create(expenses), "text/csv; charset=utf-8", fileName);
}
