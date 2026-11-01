using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Department).IsRequired().HasMaxLength(100);
            entity.Property(e => e.JobTitle).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);

            // Money needs an exact decimal type: 18 digits in total, 2 after the point.
            entity.Property(e => e.Salary).HasPrecision(18, 2);

            // Store the enum as readable text ("Active") instead of 0/1/2.
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

            // The database itself refuses duplicate emails (second line of defence after the service check).
            entity.HasIndex(e => e.Email).IsUnique();

            // FullName and Initials have no setter, so EF Core ignores them automatically.
        });
    }
}