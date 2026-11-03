using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public interface IAppSettingsService
{
    /// <summary>The saved settings. Treat as read-only: edit a <see cref="AppSettings.Clone"/> and pass it to <see cref="SaveAsync"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after a save, on the saving thread. Handlers in components must marshal with InvokeAsync.</summary>
    event Action? OnChange;

    /// <summary>Reads the settings row from the database (called once at startup).</summary>
    Task LoadAsync();

    /// <summary>Validates, stores and publishes new settings.</summary>
    Task SaveAsync(AppSettings settings);
}
