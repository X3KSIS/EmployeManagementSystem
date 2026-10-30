using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public class AppSettingsService
{
    public AppSettings Current { get; private set; } = new();

    public event Action? OnChange;

    public void SetDarkMode(bool value)
    {
        Current.DarkMode = value;
        OnChange?.Invoke();
    }

    public void Update(AppSettings settings)
    {
        Current = settings;
        OnChange?.Invoke();
    }
}