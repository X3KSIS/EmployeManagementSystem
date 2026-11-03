using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public interface IEmployeeService
{
    /// <summary>Searches, filters and sorts employees, then returns the requested page and the total match count.</summary>
    Task<PagedResult<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null);
    Task<IReadOnlyList<Employee>> GetRecentAsync(int count);
    Task<Employee?> GetByIdAsync(int id);
    Task<Employee> AddAsync(Employee employee);
    Task<bool> UpdateAsync(Employee employee);
    Task<bool> DeleteAsync(int id);

    /// <summary>Deletes every employee whose id is in <paramref name="ids"/> and returns how many were removed.</summary>
    Task<int> DeleteManyAsync(IEnumerable<int> ids);

    Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null);
    Task<EmployeeStats> GetStatsAsync();
}