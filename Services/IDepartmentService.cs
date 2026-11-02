using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public interface IDepartmentService
{
    Task<IReadOnlyList<Department>> GetAllAsync();
    Task<IReadOnlyList<DepartmentSummary>> GetSummariesAsync();
    Task<Department?> GetByIdAsync(int id);

    Task<bool> NameExistsAsync(string name, int? excludeDepartmentId = null);
    Task<Department> AddAsync(Department department);
    Task<bool> UpdateAsync(Department department);

    /// <summary>Deletes the department only if no employees belong to it.</summary>
    Task<DepartmentDeleteResult> DeleteAsync(int id);
}
