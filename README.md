# Employee Management System (Blazor Web App + MudBlazor)

Blazor Web App, **Interactive Server** rendering, MudBlazor UI, in-memory data service.

## Run it

```bash
cd EmployeeManagementSystem
dotnet restore
dotnet run
```

Open the URL printed in the terminal (e.g. https://localhost:5001). `Program.cs` targets **net8.0**; if you
have a newer SDK, change `<TargetFramework>` in the `.csproj` (net9.0 / net10.0).

## Folder layout

```
Models/        Employee, EmployeeStatus, filter/stat records
Services/      IEmployeeService + EmployeeService (all business logic, in memory)
Helpers/       Formatters (money, status text)
Components/
  Layout/      MainLayout (sidebar + top bar), NavMenu
  Shared/      Reusable: EmployeeForm, StatCard, StatusChip, EmployeeAvatar, DetailItem, ConfirmDialog
  Pages/       Home, Employees, AddEmployee, EditEmployee, EmployeeDetails, Departments
```

## Requirement checklist

| Requirement | Where |
|---|---|
| Employee model (Id, names, Department, Email, Salary) | `Models/Employee.cs` |
| List, search by name, filter by department, sort by name/salary | `Pages/Employees.razor` + `EmployeeService.GetEmployeesAsync` |
| Total count + average salary | `StatCard` on `Employees.razor`, `EmployeeService.GetStatsAsync` |
| Add form with validation (required, email, salary) | `Shared/EmployeeForm.razor` + DataAnnotations on `Employee` |
| Create / view / update / delete, in-memory service | `Services/EmployeeService.cs` |
| Reusable components, navigation, details page | `Components/Shared`, `NavMenu`, `EmployeeDetails.razor` |
| Dependency injection | `Program.cs` registration, `@inject` in pages |
| Async | Every service method returns `Task` |

## Concepts to be ready to explain

- **Render mode**: Interactive Server runs component code on the server; UI events and DOM diffs travel over a SignalR connection.
- **Components & parameters**: `[Parameter]` passes data down, `EventCallback` sends events up (`EmployeeForm`, `ConfirmDialog`).
- **Data binding**: `@bind-Value` is two-way binding (a `Value` + `ValueChanged` pair).
- **Forms**: `EditForm` + `DataAnnotationsValidator`; `OnValidSubmit` only fires when all rules pass.
- **DI lifetimes**: the service is `AddSingleton` so the in-memory list survives navigation (a shared list for all users; EF Core's DbContext would make it `Scoped`).
- **Interface-based design**: components depend on `IEmployeeService`, so the storage can change without touching the UI.
- **Lifecycle**: `OnInitializedAsync` (first load) vs `OnParametersSetAsync` (route parameter changes).
- **Routing**: `@page "/employees/{Id:int}"`; the `:int` constraint stops it from clashing with `/employees/add`.
- **C#**: records, LINQ (`Where`/`OrderBy`/`GroupBy`), switch expressions, nullable reference types, `lock`, `async/await`, `MemberwiseClone`.
- **Why copies?** The service returns clones, so cancelling an edit never changes the stored employee.

## Next step (optional enhancement)

Swap `EmployeeService` for an EF Core + SQLite implementation of the same `IEmployeeService`
(`Microsoft.EntityFrameworkCore.Sqlite`, an `AppDbContext`, `AddDbContextFactory`, register as Scoped).
