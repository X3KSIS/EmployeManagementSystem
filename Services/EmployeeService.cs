using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Services;

public class EmployeeService : IEmployeeService
{
    private readonly List<Employee> _employees = new();
    private readonly object _sync = new(); // the service is a singleton, so guard shared state
    private int _nextId = 1;

    public IReadOnlyList<string> Departments { get; } = new[]
    {
        "Engineering", "Product", "IT", "Human Resources", "Finance", "Marketing", "Sales"
    };

    public EmployeeService()
    {
        Seed();
    }

    // ---------- Read ----------

    public Task<IReadOnlyList<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null)
    {
        filter ??= new EmployeeFilter();

        List<Employee> snapshot;
        lock (_sync)
        {
            // Hand out copies so components can never modify the stored objects by accident.
            snapshot = _employees.Select(e => e.Clone()).ToList();
        }

        IEnumerable<Employee> query = snapshot;

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e =>
                e.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                e.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Department))
        {
            query = query.Where(e => e.Department == filter.Department);
        }

        IEnumerable<Employee> sorted = filter.Sort switch
        {
            EmployeeSortOption.NameDesc => query
                .OrderByDescending(e => e.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.LastName, StringComparer.OrdinalIgnoreCase),
            EmployeeSortOption.SalaryAsc => query.OrderBy(e => e.Salary),
            EmployeeSortOption.SalaryDesc => query.OrderByDescending(e => e.Salary),
            _ => query
                .OrderBy(e => e.FirstName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.LastName, StringComparer.OrdinalIgnoreCase)
        };

        return Task.FromResult<IReadOnlyList<Employee>>(sorted.ToList());
    }

    public Task<Employee?> GetByIdAsync(int id)
    {
        lock (_sync)
        {
            var found = _employees.FirstOrDefault(e => e.Id == id);
            return Task.FromResult(found?.Clone());
        }
    }

    public Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null)
    {
        lock (_sync)
        {
            var exists = _employees.Any(e =>
                e.Id != excludeEmployeeId &&
                string.Equals(e.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(exists);
        }
    }

    public Task<EmployeeStats> GetStatsAsync()
    {
        lock (_sync)
        {
            var total = _employees.Count;
            var average = total == 0 ? 0m : _employees.Average(e => e.Salary);
            var onLeave = _employees.Count(e => e.Status == EmployeeStatus.OnLeave);
            var departments = _employees.Select(e => e.Department).Distinct().Count();
            return Task.FromResult(new EmployeeStats(total, average, onLeave, departments));
        }
    }

    public Task<IReadOnlyList<DepartmentSummary>> GetDepartmentSummariesAsync()
    {
        lock (_sync)
        {
            var summaries = _employees
                .GroupBy(e => e.Department)
                .Select(g => new DepartmentSummary(
                    g.Key,
                    g.Count(),
                    g.Average(e => e.Salary),
                    g.Sum(e => e.Salary)))
                .OrderBy(s => s.Name)
                .ToList();

            return Task.FromResult<IReadOnlyList<DepartmentSummary>>(summaries);
        }
    }

    // ---------- Create / Update / Delete ----------

    public Task<Employee> AddAsync(Employee employee)
    {
        lock (_sync)
        {
            var toStore = Normalize(employee.Clone());
            toStore.Id = _nextId++;
            _employees.Add(toStore);
            return Task.FromResult(toStore.Clone());
        }
    }

    public Task<bool> UpdateAsync(Employee employee)
    {
        lock (_sync)
        {
            var index = _employees.FindIndex(e => e.Id == employee.Id);
            if (index < 0)
            {
                return Task.FromResult(false);
            }

            _employees[index] = Normalize(employee.Clone());
            return Task.FromResult(true);
        }
    }

    public Task<bool> DeleteAsync(int id)
    {
        lock (_sync)
        {
            return Task.FromResult(_employees.RemoveAll(e => e.Id == id) > 0);
        }
    }



    private static Employee Normalize(Employee e)
    {
        e.FirstName = e.FirstName.Trim();
        e.LastName = e.LastName.Trim();
        e.Email = e.Email.Trim();
        e.JobTitle = e.JobTitle.Trim();
        return e;
    }

    private void Seed()
    {
        var seed = new[]
        {
            New("Chomba", "Sikazwe", "Engineering", "Chemical Engineer", 7500, EmployeeStatus.Active),
            New("Mary", "Mulenga", "Product", "Product Manager", 8200, EmployeeStatus.Active),
            New("Alex", "Mwansa", "IT", "System Administrator", 6500, EmployeeStatus.OnLeave),
            New("Rachel", "Mwenya", "Human Resources", "HR Specialist", 5800, EmployeeStatus.Active),
            New("David", "Mutale", "Engineering", "DevOps Engineer", 7800, EmployeeStatus.Active),
            New("Grace", "Banda", "Finance", "Accountant", 6100, EmployeeStatus.Active),
            New("Peter", "Mwansa", "Sales", "Sales Executive", 5400, EmployeeStatus.Active),
            New("Linda", "Phiri", "Marketing", "Marketing Lead", 6900, EmployeeStatus.Inactive)
        };

        foreach (var e in seed)
        {
            e.Id = _nextId++;
            _employees.Add(e);
        }

        static Employee New(string first, string last, string dept, string title, decimal salary, EmployeeStatus status) => new()
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
}
