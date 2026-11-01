# Employee Management System (Blazor Web App + MudBlazor)

Blazor Web App, **Interactive Server** rendering, MudBlazor UI, SQL Server.

## Run it

```bash
cd EmployeeManagementSystem
dotnet tool install --global dotnet-ef
dotnet restore
dotnet ef migrations add InitialCreate
dotnet run --launch-profile https
```

Open the URL printed in the terminal (e.g. https://localhost:5001). `Program.cs` targets **net10.0**; if you
have a newer SDK, change `<TargetFramework>` in the `.csproj` (version).


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

## Concepts to learn

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