using EmployeeManagementSystem.Data;
using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public DepartmentService(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<Department>> GetAllAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync();
    }

    public async Task<IReadOnlyList<DepartmentSummary>> GetSummariesAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await ToSummariesAsync(db.Departments.AsNoTracking().OrderBy(d => d.Name).ThenBy(d => d.Id));
    }

    public async Task<PagedResult<DepartmentSummary>> GetSummaryPageAsync(int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, EmployeeService.MaxPageSize);

        await using var db = await _factory.CreateDbContextAsync();
        var total = await db.Departments.CountAsync();

        // A page that no longer exists (e.g. the last department on the last page was deleted)
        // falls back to the last page that does.
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pageCount);

        var items = await ToSummariesAsync(db.Departments.AsNoTracking()
            .OrderBy(d => d.Name)
            .ThenBy(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize));

        return new PagedResult<DepartmentSummary>(items, total, page, pageSize);
    }

    // Starts from Departments so empty departments are listed too (with zero employees).
    private static async Task<List<DepartmentSummary>> ToSummariesAsync(IQueryable<Department> departments)
    {
        var rows = await departments
            .Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                Count = d.Employees.Count(),
                Average = d.Employees.Average(e => (decimal?)e.Salary),
                Total = d.Employees.Sum(e => (decimal?)e.Salary)
            })
            .ToListAsync();

        return rows
            .Select(x => new DepartmentSummary(x.Id, x.Name, x.Description, x.Count, x.Average ?? 0m, x.Total ?? 0m))
            .ToList();
    }

    public async Task<Department?> GetByIdAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeDepartmentId = null)
    {
        var cleaned = name.Trim();
        var excludeId = excludeDepartmentId ?? 0;

        await using var db = await _factory.CreateDbContextAsync();
        return await db.Departments.AnyAsync(d => d.Id != excludeId && d.Name == cleaned);
    }

    public async Task<Department> AddAsync(Department department)
    {
        var entity = new Department
        {
            Name = department.Name.Trim(),
            Description = CleanDescription(department.Description)
        };

        await using var db = await _factory.CreateDbContextAsync();
        db.Departments.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<bool> UpdateAsync(Department department)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.Departments.FindAsync(department.Id);
        if (existing is null)
        {
            return false;
        }

        existing.Name = department.Name.Trim();
        existing.Description = CleanDescription(department.Description);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<DepartmentDeleteResult> DeleteAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();

        var existing = await db.Departments.FindAsync(id);
        if (existing is null)
        {
            return DepartmentDeleteResult.NotFound;
        }

        if (await db.Employees.AnyAsync(e => e.DepartmentId == id))
        {
            return DepartmentDeleteResult.HasEmployees;
        }

        db.Departments.Remove(existing);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // An employee was assigned between the check and the delete; the Restrict FK refused it.
            return DepartmentDeleteResult.HasEmployees;
        }

        return DepartmentDeleteResult.Deleted;
    }

    private static string? CleanDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
