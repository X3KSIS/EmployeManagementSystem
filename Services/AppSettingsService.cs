using EmployeeManagementSystem.Data;
using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Services;


public class AppSettingsService : IAppSettingsService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public AppSettingsService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public AppSettings Current { get; private set; } = new();

    public event Action? OnChange;

    public async Task LoadAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var row = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == AppSettings.SingletonId);

        Current = row ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var clean = settings.Clone();
        clean.Id = AppSettings.SingletonId;
        clean.CompanyName = clean.CompanyName.Trim();
        clean.WelcomeMessage = (clean.WelcomeMessage ?? string.Empty).Trim(); // a cleared text field can arrive as null

        if (clean.CompanyName.Length == 0)
        {
            throw new ArgumentException("Company name is required.", nameof(settings));
        }

        if (!AppSettings.PageSizeOptions.Contains(clean.DefaultPageSize))
        {
            clean.DefaultPageSize = AppSettings.DefaultPageSizeValue;
        }

        await using var db = await _factory.CreateDbContextAsync();
        var existing = await db.AppSettings.FindAsync(AppSettings.SingletonId);
        if (existing is null)
        {
            db.AppSettings.Add(clean);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(clean);
        }

        await db.SaveChangesAsync();

        Current = clean;
        OnChange?.Invoke();
    }
}
