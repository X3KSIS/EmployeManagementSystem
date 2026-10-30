using EmployeeManagementSystem.Models;

namespace EmployeeManagementSystem.Helpers;

public static class Formatters
{
    public static string Money(decimal amount) => $"K{amount:N0}";

    public static string StatusText(EmployeeStatus status) => status switch
    {
        EmployeeStatus.Active => "Active",
        EmployeeStatus.OnLeave => "On Leave",
        EmployeeStatus.Inactive => "Inactive",
        _ => status.ToString()
    };
}
