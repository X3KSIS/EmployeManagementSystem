using EmployeeManagementSystem.Components;
using EmployeeManagementSystem.Data;
using EmployeeManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor components with Interactive Server rendering (UI events travel over a SignalR connection).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers MudBlazor's services (ISnackbar, IDialogService, ...).
builder.Services.AddMudServices();

// EF Core + SQL Server. A DbContext *factory* is the recommended pattern for Blazor Server.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Dependency injection: components ask for IEmployeeService and never see the concrete class.
// To go back to the in-memory list, register InMemoryEmployeeService (as a Singleton) instead.
builder.Services.AddScoped<IEmployeeService, EmployeeService>();
builder.Services.AddScoped<AppSettingsService>();

var app = builder.Build();

// On startup: apply any pending migrations (creates the database and tables on first run), then seed sample data.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

// .NET 9/10: serves wwwroot AND package files such as _content/MudBlazor/*
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .WithStaticAssets()
    .AddInteractiveServerRenderMode();

app.Run();