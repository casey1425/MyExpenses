using Microsoft.EntityFrameworkCore;
using MyExpenses.Components;
using MyExpenses.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "myexpenses.db");

builder.Services.AddDbContextFactory<ExpensesDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

var app = builder.Build();

await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ExpensesDbContext>>()
    .CreateDbContextAsync())
{
    await db.Database.EnsureCreatedAsync();
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

app.Run();
