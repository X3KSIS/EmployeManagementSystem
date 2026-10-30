using EmployeeManagementSystem.Components;
using EmployeeManagementSystem.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor components with Interactive Server rendering (UI events travel over a SignalR connection).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Registers MudBlazor's services (ISnackbar, IDialogService, ...).
builder.Services.AddMudServices();

// Dependency injection: components ask for IEmployeeService and never see the concrete class.
// Singleton = ONE instance for the whole app, so the in-memory list survives page navigation
// and is shared by every browser tab. (A DbContext-backed service would be Scoped instead.)
builder.Services.AddSingleton<IEmployeeService, EmployeeService>();

var app = builder.Build();

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
