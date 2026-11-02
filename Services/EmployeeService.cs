using EmployeeManagementSystem.Data;
using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public EmployeeService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }



    public const int MaxPageSize = 100;

    public async Task<PagedResult<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null)
    {
        filter ??= new EmployeeFilter();
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync();

        
        IQueryable<Employee> query = db.Employees.AsNoTracking().Include(e => e.Department);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e =>
                (e.FirstName + " " + e.LastName).Contains(term) ||
                e.Email.Contains(term));
        }

        if (filter.DepartmentId is int departmentId)
        {
            query = query.Where(e => e.DepartmentId == departmentId);
        }

        if (filter.Status is EmployeeStatus status)
        {
            query = query.Where(e => e.Status == status);
        }

        // Count after search/filter so the total matches what the user is paging through.
        var total = await query.CountAsync();

        // If the requested page no longer exists (e.g. the last row on the last page was deleted),
        // fall back to the last page that does.
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);

        // Id is the final tie-breaker so Skip/Take is stable: rows with equal names or salaries
        // must not jump between pages.
        IQueryable<Employee> sorted = filter.Sort switch
        {
            EmployeeSortOption.NameDesc => query.OrderByDescending(e => e.FirstName).ThenByDescending(e => e.LastName).ThenBy(e => e.Id),
            EmployeeSortOption.SalaryAsc => query.OrderBy(e => e.Salary).ThenBy(e => e.Id),
            EmployeeSortOption.SalaryDesc => query.OrderByDescending(e => e.Salary).ThenBy(e => e.Id),
            _ => query.OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ThenBy(e => e.Id)
        };

        var items = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Employee>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<Employee>> GetRecentAsync(int count)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Include(e => e.Department)
            .OrderByDescending(e => e.Id)
            .Take(count)
            .ToListAsync();
    }

    public async Task<Employee?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AsNoTracking()
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null)
    {
        var cleaned = email.Trim();
        var excludeId = excludeEmployeeId ?? 0;

        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.AnyAsync(e => e.Id != excludeId && e.Email == cleaned);
    }

    public async Task<EmployeeStats> GetStatsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var total = await db.Employees.CountAsync();

        
        var average = await db.Employees.Select(e => (decimal?)e.Salary).AverageAsync() ?? 0m;

        var onLeave = await db.Employees.CountAsync(e => e.Status == EmployeeStatus.OnLeave);
        var departments = await db.Departments.CountAsync();

        return new EmployeeStats(total, average, onLeave, departments);
    }

    public async Task<Employee> AddAsync(Employee employee)
    {
        Normalize(employee);
        employee.Id = 0;
        employee.Department = null; // link by DepartmentId only; never insert the navigation

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

    public async Task<int> DeleteManyAsync(IEnumerable<int> ids)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return 0;
        }

        // One DELETE ... WHERE Id IN (...) statement, so either all rows go or none do.
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Employees.Where(e => idList.Contains(e.Id)).ExecuteDeleteAsync();
    }

    private static void Normalize(Employee e)
    {
        e.FirstName = e.FirstName.Trim();
        e.LastName = e.LastName.Trim();
        e.Email = e.Email.Trim();
        e.JobTitle = e.JobTitle.Trim();
    }
}