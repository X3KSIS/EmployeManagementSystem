using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public interface IEmployeeService
{
    IReadOnlyList<string> Departments { get; }

    Task<IReadOnlyList<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null);
    Task<Employee?> GetByIdAsync(int id);
    Task<Employee> AddAsync(Employee employee);
    Task<bool> UpdateAsync(Employee employee);
    Task<bool> DeleteAsync(int id);

    Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null);
    Task<EmployeeStats> GetStatsAsync();
    Task<IReadOnlyList<DepartmentSummary>> GetDepartmentSummariesAsync();
}