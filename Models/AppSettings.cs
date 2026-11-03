using System.ComponentModel.DataAnnotations;

namespace EmployeeManagementSystem.Models;

public enum TableDensity
{
    Comfortable,
    Compact
}

/// <summary>
/// Company-wide settings, stored as a single row (Id = 1).
/// </summary>
public class AppSettings
{
    public const int SingletonId = 1;
    public const string DefaultCompanyName = "Bangison Ltd";
    public const string DefaultWelcomeMessage = "Welcome back";
    public const int DefaultPageSizeValue = 10;

    /// <summary>The page sizes every pager offers; the default page size must be one of them.</summary>
    public static readonly int[] PageSizeOptions = { 5, 10, 25, 50 };

    public int Id { get; set; } = SingletonId;

    public bool DarkMode { get; set; }

    [Required(ErrorMessage = "Company name is required")]
    [StringLength(100, ErrorMessage = "Company name can be at most 100 characters")]
    public string CompanyName { get; set; } = DefaultCompanyName;

    /// <summary>Line under the company name in the top bar. Empty hides it.</summary>
    [StringLength(100, ErrorMessage = "Welcome message can be at most 100 characters")]
    public string WelcomeMessage { get; set; } = DefaultWelcomeMessage;

    /// <summary>Rows per page the Employees table starts with.</summary>
    public int DefaultPageSize { get; set; } = DefaultPageSizeValue;

    public TableDensity TableDensity { get; set; } = TableDensity.Comfortable;

    public bool IsCompact => TableDensity == TableDensity.Compact;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
