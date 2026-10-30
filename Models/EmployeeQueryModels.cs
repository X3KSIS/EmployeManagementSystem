namespace EmployeeManagementSystem.Models;

public enum EmployeeSortOption
{
    NameAsc,
    NameDesc,
    SalaryAsc,
    SalaryDesc
}

/// <summary>What the list page asks the service for. Records are immutable and compare by value.</summary>
public record EmployeeFilter(
    string? Search = null,
    string? Department = null,
    EmployeeSortOption Sort = EmployeeSortOption.NameAsc);

public record EmployeeStats(
    int TotalCount,
    decimal AverageSalary,
    int OnLeaveCount,
    int DepartmentCount);

public record DepartmentSummary(
    string Name,
    int EmployeeCount,
    decimal AverageSalary,
    decimal TotalPayroll);
