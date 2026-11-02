using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Departments.AnyAsync() || await db.Employees.AnyAsync())
        {
            return;
        }

        var departments = new[]
        {
            new Department { Name = "IT", Description = "Infrastructure, software and technical support" },
            new Department { Name = "Operations", Description = "Day-to-day running of the business" },
            new Department { Name = "Legal", Description = "Contracts, compliance and legal advice" },
            new Department { Name = "Human Resources", Description = "Recruitment, people and culture" },
            new Department { Name = "Finance", Description = "Accounting, budgeting and payroll" },
            new Department { Name = "Marketing", Description = "Brand, campaigns and communications" },
            new Department { Name = "Sales", Description = "Customer acquisition and accounts" }
        };
        db.Departments.AddRange(departments);
        await db.SaveChangesAsync();

        var byName = departments.ToDictionary(d => d.Name);
        Employee New(string first, string last, string dept, string title, decimal salary, EmployeeStatus status)
            => Create(first, last, byName[dept].Id, title, salary, status);

        db.Employees.AddRange(
            New("John", "Chibangu", "IT", "Mechanical Engineer", 7500, EmployeeStatus.Active),
            New("Mary", "Sikazwe", "Legal", "Product Manager", 8200, EmployeeStatus.Active),
            New("Alex", "Mwape", "Operations", "UI/UX Designer", 6500, EmployeeStatus.OnLeave),
            New("Rachel", "Mwenya", "Human Resources", "HR Specialist", 5800, EmployeeStatus.Active),
            New("David", "Mutale", "IT", "DevOps Engineer", 7800, EmployeeStatus.Active),
            New("Grace", "Banda", "Finance", "Accountant", 6100, EmployeeStatus.Active),
            New("Peter", "Mwansa", "Sales", "Sales Executive", 5400, EmployeeStatus.Active),
            New("Linda", "Phiri", "Marketing", "Marketing Lead", 6900, EmployeeStatus.Inactive));

        await db.SaveChangesAsync();
    }

    private static Employee Create(string first, string last, int departmentId, string title, decimal salary, EmployeeStatus status) => new()
    {
        FirstName = first,
        LastName = last,
        DepartmentId = departmentId,
        JobTitle = title,
        Salary = salary,
        Status = status,
        Email = $"{first}.{last}@bangison.com".ToLowerInvariant(),
        DateHired = DateTime.Today.AddMonths(-Random.Shared.Next(2, 48))
    };
}