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
    int? DepartmentId = null,
    EmployeeSortOption Sort = EmployeeSortOption.NameAsc,
    int Page = 1,
    int PageSize = 10,
    EmployeeStatus? Status = null); // null = all statuses

/// <summary>One page of results plus the total number of matches across all pages.</summary>
public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public int FirstItemNumber => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItemNumber => (Page - 1) * PageSize + Items.Count;
}

public record EmployeeStats(
    int TotalCount,
    decimal AverageSalary,
    int OnLeaveCount,
    int DepartmentCount);

public enum DepartmentDeleteResult
{
    Deleted,
    NotFound,
    HasEmployees
}

public record DepartmentSummary(
    int Id,
    string Name,
    string? Description,
    int EmployeeCount,
    decimal AverageSalary,
    decimal TotalPayroll);
