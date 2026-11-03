using System.ComponentModel.DataAnnotations;

namespace EmployeeManagementSystem.Models;

public class Department
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Department name is required")]
    [StringLength(100, ErrorMessage = "Department name can be at most 100 characters")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description can be at most 500 characters")]
    public string? Description { get; set; }

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
