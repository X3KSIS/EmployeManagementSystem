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

        // Starts from Departments so empty departments are listed too (with zero employees).
        var rows = await db.Departments
            .AsNoTracking()
            .OrderBy(d => d.Name)
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
