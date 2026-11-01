using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Data;

public static class DbSeeder
{
    /// <summary>Adds sample employees, but only when the table is empty (so it runs once).</summary>
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Employees.AnyAsync())
        {
            return;
        }

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

    private static Employee New(string first, string last, string dept, string title, decimal salary, EmployeeStatus status) => new()
    {
        FirstName = first,
        LastName = last,
        Department = dept,
        JobTitle = title,
        Salary = salary,
        Status = status,
        Email = $"{first}.{last}@bangison.com".ToLowerInvariant(),
        DateHired = DateTime.Today.AddMonths(-Random.Shared.Next(2, 48))
    };
}