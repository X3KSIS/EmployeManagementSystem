using EmployeeManagementSystem.Data;
using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Services;

/// <summary>
/// SQL Server implementation (EF Core). Same contract as the in-memory version,
/// so no page or component needed to change.
/// </summary>
public class EmployeeService : IEmployeeService
{
    // In Blazor Server a component can outlive many user actions, so a long-lived DbContext is unsafe.
    // We take a factory and create a short-lived context per operation.
    private readonly IDbContextFactory<AppDbContext> _factory;

    public IReadOnlyList<string> Departments { get; } = new[]
    {
        "IT", "Legal", "Operations", "Human Resources", "Finance", "Marketing", "Sales"
    };

    public EmployeeService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    // ---------- Read ----------

    public async Task<IReadOnlyList<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null)
    {
        filter ??= new EmployeeFilter();

        await using var db = await _factory.CreateDbContextAsync();

        // AsNoTracking: we only read these, so EF doesn't need to watch them for changes.
        IQueryable<Employee> query = db.Employees.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            // SQL Server's default collation is case-insensitive, so this matches "john" and "JOHN".
            query = query.Where(e =>
                (e.FirstName + " " + e.LastName).Contains(term) ||
                e.Email.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(filter.Department))
        {
            var department = filter.Department;
            query = query.Where(e => e.Department == department);
        }

        IQueryable<Employee> sorted = filter.Sort switch
        {
            EmployeeSortOption.NameDesc => query.OrderByDescending(e => e.FirstName).ThenByDescending(e => e.LastName),
            EmployeeSortOption.SalaryAsc => query.OrderBy(e => e.Salary),
            EmployeeSortOption.SalaryDesc => query.OrderByDescending(e => e.Salary),
            _ => query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
        };

        return await sorted.ToListAsync();
    }

    public async Task<Employee?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null)
    {
        var cleaned = email.Trim();
        var excludeId = excludeEmployeeId ?? 0; // ids start at 1, so 0 excludes nobody

        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AnyAsync(e => e.Id != excludeId && e.Email == cleaned);
    }

    public async Task<EmployeeStats> GetStatsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var total = await db.Employees.CountAsync();

        // Cast to decimal? so an empty table gives null instead of throwing.
        var average = await db.Employees.Select(e => (decimal?)e.Salary).AverageAsync() ?? 0m;

        var onLeave = await db.Employees.CountAsync(e => e.Status == EmployeeStatus.OnLeave);
        var departments = await db.Employees.Select(e => e.Department).Distinct().CountAsync();

        return new EmployeeStats(total, average, onLeave, departments);
    }

    public async Task<IReadOnlyList<DepartmentSummary>> GetDepartmentSummariesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        // GroupBy is translated to a SQL GROUP BY, so the database does the maths.
        return await db.Employees
            .GroupBy(e => e.Department)
            .Select(g => new DepartmentSummary(
                g.Key,
                g.Count(),
                g.Average(e => e.Salary),
                g.Sum(e => e.Salary)))
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    // ---------- Create / Update / Delete ----------

    public async Task<Employee> AddAsync(Employee employee)
    {
        Normalize(employee);
        employee.Id = 0; // let SQL Server generate the identity value

        await using var db = await _factory.CreateDbContextAsync();
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    public async Task<bool> UpdateAsync(Employee employee)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.Employees.FindAsync(employee.Id);
        if (existing is null)
        {
            return false;
        }

        Normalize(employee);
        db.Entry(existing).CurrentValues.SetValues(employee);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var rows = await db.Employees.Where(e => e.Id == id).ExecuteDeleteAsync();
        return rows > 0;
    }

    private static void Normalize(Employee e)
    {
        e.FirstName = e.FirstName.Trim();
        e.LastName = e.LastName.Trim();
        e.Email = e.Email.Trim();
        e.JobTitle = e.JobTitle.Trim();
    }
}