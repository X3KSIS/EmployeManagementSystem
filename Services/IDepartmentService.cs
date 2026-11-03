using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public interface IDepartmentService
{
    Task<IReadOnlyList<Department>> GetAllAsync();
    /// <summary>Every department with its employee count and pay totals (for charts and reports).</summary>
    Task<IReadOnlyList<DepartmentSummary>> GetSummariesAsync();

    /// <summary>One page of department summaries, ordered by name, plus the total department count.</summary>
    Task<PagedResult<DepartmentSummary>> GetSummaryPageAsync(int page, int pageSize);
    Task<Department?> GetByIdAsync(int id);

    Task<bool> NameExistsAsync(string name, int? excludeDepartmentId = null);
    Task<Department> AddAsync(Department department);
    Task<bool> UpdateAsync(Department department);

    /// <summary>Deletes the department only if no employees belong to it.</summary>
    Task<DepartmentDeleteResult> DeleteAsync(int id);
}
