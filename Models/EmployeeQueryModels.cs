namespace EmployeeManagementSystem.Models;

public enum EmployeeSortOption
{
    NameAsc,
    NameDesc,
    SalaryAsc,
    SalaryDesc
}


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
