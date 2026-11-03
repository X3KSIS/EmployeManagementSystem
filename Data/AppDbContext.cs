using EmployeeManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeManagementSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedNever(); // single row, always Id = 1
            entity.Property(s => s.CompanyName).IsRequired().HasMaxLength(100);
            entity.Property(s => s.WelcomeMessage).IsRequired().HasMaxLength(100); // NOT NULL, but may be empty
            entity.Property(s => s.TableDensity).HasConversion<string>().HasMaxLength(20);

            // The migration creates the row, so there is always exactly one to read.
            entity.HasData(new AppSettings
            {
                Id = Models.AppSettings.SingletonId,
                DarkMode = false,
                CompanyName = Models.AppSettings.DefaultCompanyName,
                WelcomeMessage = Models.AppSettings.DefaultWelcomeMessage,
                DefaultPageSize = Models.AppSettings.DefaultPageSizeValue,
                TableDensity = TableDensity.Comfortable
            });
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Name).IsRequired().HasMaxLength(100);
            entity.Property(d => d.Description).HasMaxLength(500);
            entity.HasIndex(d => d.Name).IsUnique();
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(50);
            entity.HasOne(e => e.Department)
                  .WithMany(d => d.Employees)
                  .HasForeignKey(e => e.DepartmentId)
                  .IsRequired()
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.JobTitle).IsRequired().HasMaxLength(80);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Salary).HasPrecision(18, 2);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.Email).IsUnique();
        });
    }
}