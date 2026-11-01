using System.ComponentModel.DataAnnotations;

namespace EmployeeManagementSystem.Models;

public enum EmployeeStatus
{
    Active,
    OnLeave,
    Inactive
}

public class Employee
{
    public int Id { get; set; }

    [Required(ErrorMessage = "First name is required")]
    [StringLength(50, ErrorMessage = "First name can be at most 50 characters")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required")]
    [StringLength(50, ErrorMessage = "Last name can be at most 50 characters")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a department")]
    public string Department { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    [Range(1, 10_000_000, ErrorMessage = "Salary must be greater than zero")]
    public decimal Salary { get; set; }


    [Required(ErrorMessage = "Job title is required")]
    [StringLength(80)]
    public string JobTitle { get; set; } = string.Empty;

    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public DateTime? DateHired { get; set; } = DateTime.Today;

    
    public string FullName => $"{FirstName} {LastName}".Trim();

    public string Initials =>
        $"{(FirstName.Length > 0 ? FirstName[0] : ' ')}{(LastName.Length > 0 ? LastName[0] : ' ')}"
            .Trim()
            .ToUpperInvariant();

    
    public Employee Clone() => (Employee)MemberwiseClone();
}
