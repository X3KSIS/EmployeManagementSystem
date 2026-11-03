# Employee Management System — Overview

> Line numbers refer to the code as of this document's writing (on top of commit `a9ef283`, plus the uncommitted
> Settings / department-paging work). They will drift as the code changes.

---

## 1. Project overview

**What it does.** A web application for managing a small company's staff. It keeps a list of employees and the
departments they belong to, and lets an administrator add, edit, search, filter, page through and delete them. It
also shows headcount and payroll figures per department, and has a few company-wide settings.

**Who it's for.** An HR or office administrator at a single company (the seed data is for "Bangison Ltd"). There is
no login: everyone who can reach the site has full access, and settings are shared by all users.

**Main features**

| Area | Features |
|---|---|
| Employees | Create, view, edit, delete; search by name or email; filter by department and status; sort by name or salary; server-side paging; bulk delete with row checkboxes |
| Departments | Create, rename, delete; employee count, average salary and payroll per department; delete is refused while a department still has employees; paging |
| Dashboard | Headline stats (total employees, average salary, departments, on leave), employees-per-department chart, recently added employees |
| Reports | Employees-per-department chart plus a table of headcount, average salary and total payroll |
| Settings | Light/dark theme, table density, default rows per page, company name and welcome message in the top bar — stored in the database and pushed live to every open tab |

**Tech stack**

| Layer | Technology |
|---|---|
| Language | C# (with nullable reference types and implicit usings enabled) |
| Runtime / framework | .NET 10 (`net10.0`), ASP.NET Core |
| UI | Blazor Web App, **Interactive Server** render mode (UI runs on the server; browser events travel over a SignalR connection) |
| Component library | MudBlazor 8.15.0 (Material Design components: tables, forms, charts, theme, snackbars) |
| Data access | Entity Framework Core 10.0.14 (`Microsoft.EntityFrameworkCore.SqlServer`), code-first migrations |
| Database | Microsoft SQL Server (local instance, Windows authentication — see `appsettings.json`) |
| Tooling | `dotnet-ef` CLI via `Microsoft.EntityFrameworkCore.Design`; `dotnet watch` for hot reload |

---

## 2. Important folders

The structure is a **layered architecture** in a single project:

```
Components (presentation)  →  Services (business logic, behind interfaces)  →  Data (EF Core DbContext)  →  SQL Server
                       ↘              ↓                    ↙
                                Models (shared entities and DTOs)
```

Pages never touch `AppDbContext`; they only call service interfaces. It is not Clean Architecture: there is
one project, and the services depend directly on EF Core rather than on repository abstractions.

| Folder | What it contains | Why it matters |
|---|---|---|
| `Components/` | Root of the Blazor UI: `App.razor` (HTML host page), `Routes.razor` (router), `_Imports.razor` (shared `@using`s) | Entry point of the presentation layer; sets the render mode for the whole app |
| `Components/Layout/` | `MainLayout.razor` (theme, app bar, drawer), `NavMenu.razor` | The shell every page renders inside; applies the dark-mode and company-name settings |
| `Components/Pages/` | Routable pages (`@page`): Home (dashboard), Employees, Add/Edit/Details, Departments, Reports, Settings | One file per screen; each injects service interfaces and owns its page state |
| `Components/Shared/` | Reusable components: `EmployeeForm`, `TablePager`, `ConfirmDialog`, `DepartmentHeadcountChart`, `StatCard`, … | Shared UI so pages don't duplicate forms, pagers and dialogs |
| `Services/` | `I…Service` interfaces and their EF Core implementations | Business layer: queries, validation rules (e.g. "can't delete a non-empty department"), paging |
| `Data/` | `AppDbContext` (EF Core model configuration), `DbSeeder` (sample data) | Data-access layer: table mapping, keys, indexes, relationships, delete rules |
| `Models/` | Entities (`Employee`, `Department`, `AppSettings`), enums, and query/result records (`EmployeeFilter`, `PagedResult<T>`, `DepartmentSummary`) | The types shared by every layer; validation attributes live here |
| `Migrations/` | EF Core code-first migrations and the model snapshot | Versioned database schema; applied automatically at startup |
| `Helpers/` | `Formatters` (currency and status display text) | Presentation helpers shared by several pages |
| `wwwroot/` | `app.css` | Static assets; the custom styles use MudBlazor theme variables so they follow dark mode |

---

## 3. Startup flow

Entry point: **`Program.cs`**, written as top-level statements (the compiler generates the `Main` method).

| # | Step | Where | Mechanism |
|---|---|---|---|
| 1 | Create the host builder; load `appsettings.json` (+ `appsettings.{Environment}.json`, environment variables, command line) and set up logging and the Kestrel web server | `Program.cs:7` | `WebApplication.CreateBuilder` — **configuration providers** and the generic host |
| 2 | Register Blazor with Interactive Server support | `Program.cs:10-11` | **DI registration** via extension methods (`AddRazorComponents().AddInteractiveServerComponents()`) |
| 3 | Register MudBlazor services (snackbar, dialogs, popovers, breakpoint service, …) | `Program.cs:14` | **DI registration** (`AddMudServices`) |
| 4 | Register an EF Core **context factory** for SQL Server, reading the connection string from configuration | `Program.cs:17-18` | **DI registration** + **configuration lookup** (`GetConnectionString("DefaultConnection")`) |
| 5 | Register application services: `IEmployeeService` and `IDepartmentService` as **scoped**, `IAppSettingsService` as **singleton** | `Program.cs:21-23` | **DI registration** with explicit lifetimes |
| 6 | Build the app (freezes the service collection into a service provider) | `Program.cs:25` | `builder.Build()` |
| 7 | Create a temporary DI scope, apply pending migrations (creates the database on first run), then seed sample data if the tables are empty | `Program.cs:28-34`, `Data/DbSeeder.cs:8` | **Manual DI scope**, `Database.MigrateAsync()`, top-level `await` |
| 8 | Load the settings row into the singleton, so the very first render already has the saved theme and company name | `Program.cs:37`, `Services/AppSettingsService.cs:21` | Resolving a **singleton** from the root provider |
| 9 | Add HSTS (production only), HTTPS redirection and anti-forgery protection | `Program.cs:39-45` | **Middleware pipeline** |
| 10 | Map static files (with build-time fingerprinting) and the root Razor component with Interactive Server rendering | `Program.cs:48-52` | **Endpoint routing** (`MapStaticAssets`, `MapRazorComponents<App>`) |
| 11 | Start listening (`https://localhost:7100` / `http://localhost:5100` in development, from `Properties/launchSettings.json`) | `Program.cs:54` | `app.Run()` |

**What happens per browser visit**

1. `App.razor` is served as the HTML page. It loads MudBlazor's CSS/JS and `blazor.web.js`
   (`Components/App.razor:11-19`).
2. `<Routes @rendermode="InteractiveServer" />` (`App.razor:17`) prerenders the page on the server, then
   `blazor.web.js` opens a **SignalR circuit**. After that, clicks and typing are sent to the server, and only UI diffs
   come back.
3. `Routes.razor` matches the URL against each component's `@page` directive and renders it inside `MainLayout`
   (`Components/Routes.razor:1-3`).

Not used: there are no hosted/background services, no options classes bound with `Configure<T>`, and no authentication
middleware.

---

## 4. Build process

**Build configuration.** There is a single project file, `EmployeeManagementSystem.csproj`. There is **no solution
(`.sln`) file, no Dockerfile and no custom MSBuild targets or scripts**.

| Setting (`.csproj`) | Value | Effect |
|---|---|---|
| `Sdk` | `Microsoft.NET.Sdk.Web` | ASP.NET Core web SDK: Razor compilation, static web assets, `launchSettings.json` support |
| `TargetFramework` | `net10.0` | .NET 10 |
| `Nullable` | `enable` | Nullable reference type warnings across the project |
| `ImplicitUsings` | `enable` | Auto-imports `System`, `System.Linq`, `System.Threading.Tasks`, ASP.NET Core namespaces, … |
| `MudBlazor` | `8.*` (resolved 8.15.0) | UI component library |
| `Microsoft.EntityFrameworkCore.SqlServer` | `10.*` (resolved 10.0.14) | EF Core + SQL Server provider |
| `Microsoft.EntityFrameworkCore.Design` | `10.*`, `PrivateAssets=all` | Design-time support for `dotnet ef`; not shipped with the app |
| `System.Security.Cryptography.Pkcs` | `9.0.21` (pinned) | Pinned to a patched version to fix a transitive security advisory (commit `61a51dc`) |

Note that the floating versions (`8.*`, `10.*`) mean two builds on different days can pull different package
versions.

**Commands**

| Command | What it does |
|---|---|
| `dotnet build` | Restores packages and compiles the C# and Razor files |
| `dotnet run --launch-profile https` | Builds, applies migrations, seeds data and serves on ports 7100/5100 |
| `dotnet watch run --launch-profile https` | Same as above, with hot reload on file changes |
| `dotnet ef migrations add <Name>` | Generates a new migration in `Migrations/` from model changes |
| `dotnet ef database update` | Applies migrations by hand (normally unnecessary: startup calls `MigrateAsync`) |
| `dotnet publish -c Release` | Produces a deployable folder (no publish profile is configured) |

**Outputs**

- `bin/Debug/net10.0/`: `EmployeeManagementSystem.dll` (the app), `EmployeeManagementSystem.exe` (native launcher),
  `.pdb` (debug symbols), `.deps.json`, `.runtimeconfig.json`, and the `staticwebassets.*.json` manifests used by
  `MapStaticAssets`.
- `obj/`: intermediate files (restore graph `project.assets.json`, generated assembly info, compiled Razor).
- `bin/` and `obj/` are currently **tracked in git** (232 files). `.gitignore` lists only `EMS_Complete_Code_Guide.md`.

---

## 5. Key files

| Path | Role | Why it matters | Key exports / members |
|---|---|---|---|
| `Program.cs` | Composition root and entry point | Every service registration, the startup database work and the middleware pipeline | Top-level statements; DI registrations; `MigrateAsync`; `MapRazorComponents<App>` |
| `EmployeeManagementSystem.csproj` | Build definition | Framework, language features and package versions | `net10.0`, `Nullable`, `ImplicitUsings`, 4 package references |
| `appsettings.json` | Configuration | Database connection string and log levels | `ConnectionStrings:DefaultConnection` |
| `Data/AppDbContext.cs` | EF Core unit of work and model configuration | Defines tables, keys, unique indexes, FK delete rules and enum storage | `DbSet<Employee> Employees`, `DbSet<Department> Departments`, `DbSet<AppSettings> AppSettings`, `OnModelCreating` |
| `Data/DbSeeder.cs` | Sample data | Creates 7 departments and 8 employees on an empty database | `static Task SeedAsync(AppDbContext)` |
| `Models/Employee.cs` | Employee entity | Central domain type; carries the form validation rules | `Employee`, `EmployeeStatus`, `FullName`, `Initials`, `Clone()` |
| `Models/Department.cs` | Department entity | Parent side of the Employee → Department relationship | `Department`, `Employees` collection |
| `Models/EmployeeQueryModels.cs` | Query inputs and result shapes | Contract between pages and services for filtering, paging and summaries | `EmployeeFilter`, `PagedResult<T>`, `EmployeeStats`, `DepartmentSummary`, `EmployeeSortOption`, `DepartmentDeleteResult` |
| `Models/AppSettings.cs` | Settings entity (single row) | Defaults and allowed values for every setting | `AppSettings`, `TableDensity`, `PageSizeOptions`, `SingletonId`, `Clone()` |
| `Services/IEmployeeService.cs` + `EmployeeService.cs` | Employee business logic | Search/filter/sort/page query, CRUD, bulk delete, stats | `GetEmployeesAsync`, `GetRecentAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `DeleteManyAsync`, `EmailExistsAsync`, `GetStatsAsync`, `MaxPageSize` |
| `Services/IDepartmentService.cs` + `DepartmentService.cs` | Department business logic | Summaries with aggregates, paging, and the "no delete while employees exist" rule | `GetAllAsync`, `GetSummariesAsync`, `GetSummaryPageAsync`, `NameExistsAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync` |
| `Services/IAppSettingsService.cs` + `AppSettingsService.cs` | Settings store (singleton) | Loads once, saves to the DB, broadcasts changes to all open tabs | `Current`, `event OnChange`, `LoadAsync`, `SaveAsync` |
| `Components/App.razor` | HTML host page | Sets the Interactive Server render mode and loads CSS/JS | `<Routes @rendermode="InteractiveServer" />` |
| `Components/Routes.razor` | Router | Maps URLs to `@page` components; sets the default layout and 404 page | `Router`, `RouteView`, `NotFound` |
| `Components/_Imports.razor` | Shared namespaces for all `.razor` files | Why pages can use `MudBlazor`, `Models`, `Services` without `@using` | 16 `@using` lines |
| `Components/Layout/MainLayout.razor` | App shell | Theme (light/dark palettes), top bar, drawer; subscribes to settings changes | `_theme`, `HandleSettingsChanged`, `Dispose` |
| `Components/Pages/Employees.razor` | Main employee list | Uses most features at once: filters, paging, bulk delete, density | `LoadAsync`, `On…Changed` handlers, `DeleteSelectedAsync`, `DeleteConfirmedAsync` |
| `Components/Pages/Departments.razor` | Department management | Add/edit form, disabled-delete tooltip, paging | `SaveAsync`, `DeleteConfirmedAsync`, `DeleteTooltip`, `LoadAsync` |
| `Components/Pages/Settings.razor` | Settings UI | Instant-save controls plus the validated company form | `SaveAsync(Action<AppSettings>, string)`, `SaveCompanyAsync` |
| `Components/Shared/EmployeeForm.razor` | Add/edit form | One form for both create and edit; runs DataAnnotations validation | Parameters `Model`, `Departments`, `OnSubmit`, `OnCancel` |
| `Components/Shared/TablePager.razor` | Generic pager | Shared by the Employees and Departments tables | `@typeparam T`; parameters `Result`, `PageChanged`, `PageSizeChanged` |

---

## 6. Imports

In `.cs` files these are `using` directives. In `.razor` files, namespaces come from `Components/_Imports.razor` (listed
once below) and dependencies arrive through `@inject` (shown in §5 and §8), so most pages have no `@using` lines of
their own. Namespaces added by `ImplicitUsings` (such as `System`, `System.Linq` and `System.Threading.Tasks`) are not listed.

"Local" means a namespace from this project; "External" means the .NET base class library or a NuGet package.

| Importing file | Import | Local / External | Purpose |
|---|---|---|---|
| `Program.cs` | `EmployeeManagementSystem.Components` | Local | The root component `App` for `MapRazorComponents<App>()` |
| `Program.cs` | `EmployeeManagementSystem.Data` | Local | `AppDbContext`, `DbSeeder` |
| `Program.cs` | `EmployeeManagementSystem.Services` | Local | Service interfaces and implementations to register |
| `Program.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | `UseSqlServer`, `IDbContextFactory<T>`, `MigrateAsync` |
| `Program.cs` | `MudBlazor.Services` | External (MudBlazor) | `AddMudServices()` extension method |
| `Data/AppDbContext.cs` | `EmployeeManagementSystem.Models` | Local | Entity types to map |
| `Data/AppDbContext.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | `DbContext`, `DbSet<T>`, `ModelBuilder`, `DeleteBehavior` |
| `Data/DbSeeder.cs` | `EmployeeManagementSystem.Models` | Local | `Employee`, `Department`, `EmployeeStatus` |
| `Data/DbSeeder.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | `AnyAsync` |
| `Models/Employee.cs` | `System.ComponentModel.DataAnnotations` | External (BCL) | `[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]` |
| `Models/Department.cs` | `System.ComponentModel.DataAnnotations` | External (BCL) | `[Required]`, `[StringLength]` |
| `Models/AppSettings.cs` | `System.ComponentModel.DataAnnotations` | External (BCL) | `[Required]`, `[StringLength]` |
| `Helpers/Formatters.cs` | `EmployeeManagementSystem.Models` | Local | `EmployeeStatus` |
| `Services/IEmployeeService.cs` | `EmployeeManagementSystem.Models` | Local | Types in the method signatures |
| `Services/EmployeeService.cs` | `EmployeeManagementSystem.Data` | Local | `AppDbContext` |
| `Services/EmployeeService.cs` | `EmployeeManagementSystem.Models` | Local | Entities, `EmployeeFilter`, `PagedResult<T>` |
| `Services/EmployeeService.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | `IDbContextFactory<T>`, `Include`, `AsNoTracking`, `ToListAsync`, `ExecuteDeleteAsync` |
| `Services/IDepartmentService.cs` | `EmployeeManagementSystem.Models` | Local | Types in the method signatures |
| `Services/DepartmentService.cs` | `EmployeeManagementSystem.Data` | Local | `AppDbContext` |
| `Services/DepartmentService.cs` | `EmployeeManagementSystem.Models` | Local | `Department`, `DepartmentSummary`, `DepartmentDeleteResult` |
| `Services/DepartmentService.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | Async LINQ operators, `DbUpdateException` |
| `Services/IAppSettingsService.cs` | `EmployeeManagementSystem.Models` | Local | `AppSettings` |
| `Services/AppSettingsService.cs` | `EmployeeManagementSystem.Data` | Local | `AppDbContext` |
| `Services/AppSettingsService.cs` | `EmployeeManagementSystem.Models` | Local | `AppSettings` |
| `Services/AppSettingsService.cs` | `Microsoft.EntityFrameworkCore` | External (EF Core) | `IDbContextFactory<T>`, `FirstOrDefaultAsync`, `FindAsync` |
| `Components/_Imports.razor` | `System.Net.Http` | External (BCL) | Template default; not used by this app |
| `Components/_Imports.razor` | `System.Net.Http.Json` | External (BCL) | Template default; not used by this app |
| `Components/_Imports.razor` | `Microsoft.AspNetCore.Components.Forms` | External (ASP.NET Core) | `EditForm`, `DataAnnotationsValidator` |
| `Components/_Imports.razor` | `Microsoft.AspNetCore.Components.Routing` | External (ASP.NET Core) | `Router`, `NavLinkMatch`, `FocusOnNavigate` |
| `Components/_Imports.razor` | `Microsoft.AspNetCore.Components.Web` | External (ASP.NET Core) | `PageTitle`, `HeadOutlet`, DOM event args |
| `Components/_Imports.razor` | `static Microsoft.AspNetCore.Components.Web.RenderMode` | External (ASP.NET Core) | Lets `App.razor` write `InteractiveServer` unqualified |
| `Components/_Imports.razor` | `Microsoft.AspNetCore.Components.Web.Virtualization` | External (ASP.NET Core) | Template default; not used by this app |
| `Components/_Imports.razor` | `Microsoft.JSInterop` | External (ASP.NET Core) | Template default; not used directly by this app |
| `Components/_Imports.razor` | `MudBlazor` | External (MudBlazor) | Every `Mud*` component, `Color`, `Severity`, `ChartSeries`, … |
| `Components/_Imports.razor` | `EmployeeManagementSystem` | Local | Root namespace |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Components` | Local | Root components |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Components.Layout` | Local | `MainLayout`, `NavMenu` |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Components.Shared` | Local | `EmployeeForm`, `TablePager`, `ConfirmDialog`, … |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Helpers` | Local | `Formatters` |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Models` | Local | Entities and records used in markup |
| `Components/_Imports.razor` | `EmployeeManagementSystem.Services` | Local | Service interfaces for `@inject` |

---

## 7. C# and .NET concepts used

Grouped by concept. Each row gives a location, what the code does there, and why it was likely chosen, where the
code makes that apparent.

### Dependency injection and service lifetimes

| Where | What it does here | Why this choice |
|---|---|---|
| `Program.cs:17-23` | Registers the context factory and the three services against their interfaces | Pages ask for `IEmployeeService`, never for a concrete class or the database |
| `Services/EmployeeService.cs:11-14` | **Constructor injection** of `IDbContextFactory<AppDbContext>` | Dependencies are explicit and can be replaced in tests |
| `Components/Pages/Employees.razor:2-5` | `@inject` gives the page its services | The Razor equivalent of constructor injection |
| `Program.cs:21-22` | `AddScoped`: in Blazor Server, one instance **per browser circuit** (tab) | Stateless services, so one per user session is cheap and isolated |
| `Program.cs:23` | `AddSingleton`: one `AppSettingsService` for the whole app | Settings are company-wide, read on every render, and a save must reach every tab (`AppSettingsService.cs` comment, `MainLayout.razor:105-106`) |

### Interfaces (programming to an abstraction)

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/IEmployeeService.cs:5`, `IDepartmentService.cs:5`, `IAppSettingsService.cs:5` | Define each service's contract; pages depend only on these | The project history shows the employee service was first an in-memory implementation and was swapped for SQL Server without changing any page (the doc comment removed in commit `25d6be2`) |

### async/await and Task

| Where | What it does here | Why this choice |
|---|---|---|
| Every service method, e.g. `EmployeeService.GetEmployeesAsync` (`EmployeeService.cs:20-72`) | Database calls are awaited (`CountAsync`, `ToListAsync`, `SaveChangesAsync`) | In Blazor Server, every user shares the server's threads, so blocking on I/O would stall other circuits |
| `Program.cs:31-37` | **Top-level `await`** during startup (migrate, seed, load settings) | Startup can await without a `Main` method or `.Result` |
| `Components/Pages/Employees.razor:176` | `OnInitializedAsync` loads data before and during the first render | The Blazor lifecycle hook for async initialization |
| `Components/Layout/MainLayout.razor:107` | `_ = InvokeAsync(...)` **discards** the returned task on purpose | Fire-and-forget re-render from an event handler that must stay `void` |

### `using` / `await using` and IDisposable / IAsyncDisposable

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/EmployeeService.cs:25` (and every service method) | `await using var db = await _factory.CreateDbContextAsync();` creates a context for one operation and disposes it asynchronously | Short-lived contexts release connections promptly (see "EF Core context factory" below) |
| `Program.cs:28` | `using (var scope = app.Services.CreateScope())` | Scoped services need a scope outside a request; disposing it cleans them up |
| `Components/Layout/MainLayout.razor:2, 113` | `@implements IDisposable`; `Dispose()` unsubscribes from `OnChange` | The singleton settings service holds a reference to every subscriber, so without this each closed tab's layout would never be garbage-collected |

### EF Core: DbContext, Fluent API and migrations

| Where | What it does here | Why this choice |
|---|---|---|
| `Data/AppDbContext.cs:6-14` | `DbContext` subclass with `DbSet<T>` properties (expression-bodied `=> Set<T>()`) | One class describes the whole database model |
| `Data/AppDbContext.cs:16-63` | **Fluent API** in `OnModelCreating`: keys, max lengths, unique indexes on `Email` and department `Name` | Keeps database rules out of the entity classes and enforces uniqueness in SQL |
| `Data/AppDbContext.cs:52-56` | Required FK `Employee → Department` with `DeleteBehavior.Restrict` | The database itself refuses to delete a department that still has employees |
| `Data/AppDbContext.cs:24, 60` | `HasConversion<string>()` stores enums as text (`"OnLeave"`, `"Compact"`) | Readable rows, and reordering enum members can't corrupt data |
| `Data/AppDbContext.cs:59` | `HasPrecision(18, 2)` on `Salary` | Exact money storage (`decimal(18,2)`) |
| `Data/AppDbContext.cs:21, 27-35` | `ValueGeneratedNever()` + `HasData(...)` seeds the single settings row | The migration creates the row, so there is always exactly one to read |
| `Migrations/*`, `Program.cs:32` | Code-first migrations applied at startup with `MigrateAsync()` | Schema versions live in source control; first run creates the database |

### EF Core context factory (instead of an injected DbContext)

| Where | What it does here | Why this choice |
|---|---|---|
| `Program.cs:17`, `Services/EmployeeService.cs:9` | `AddDbContextFactory` + `IDbContextFactory<AppDbContext>`; each method creates its own context | A Blazor Server circuit lives for the whole tab session, and `DbContext` is not thread-safe and grows its change tracker over time. An earlier version of the service said so in a comment ("a long-lived DbContext is unsafe"). The factory is also safe to use from the singleton `AppSettingsService`. |

### LINQ and IQueryable (deferred execution)

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/EmployeeService.cs:28-46` | Builds an `IQueryable<Employee>` step by step, adding `Where` clauses only for the filters that are set | Nothing runs until `CountAsync`/`ToListAsync`; EF Core turns the whole chain into **one SQL query** |
| `Services/EmployeeService.cs:58-69` | `OrderBy…ThenBy(e => e.Id)`, then `Skip`/`Take` | Server-side paging; the `Id` tie-breaker keeps pages stable when names or salaries are equal (comment at `:56-57`) |
| `Services/DepartmentService.cs:52-66` | Projects to an **anonymous type** with `Count()`, `Average()`, `Sum()` in SQL, then maps to a record in memory | Commit `4c3083c` made this query EF-translatable; the `?? 0m` defaults for empty departments are applied in memory |
| `Components/Shared/DepartmentHeadcountChart.razor:66-74` | LINQ to Objects over an in-memory list | Sorting and shaping data for the chart |

### EF Core query and command features

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/EmployeeService.cs:28, 87-88` | `AsNoTracking()` | Read-only queries skip change tracking (faster, less memory) |
| `Services/EmployeeService.cs:28, 88` | `Include(e => e.Department)` (**eager loading**) | Loads the department name in the same query; there is no lazy loading |
| `Services/EmployeeService.cs:147, 161` | `ExecuteDeleteAsync()` | Runs a single `DELETE … WHERE` in SQL without loading the entities; bulk delete is one statement, so all rows go or none do |
| `Services/EmployeeService.cs:132-140` | `FindAsync` + `Entry(existing).CurrentValues.SetValues(employee)` | Updates a tracked entity from a detached copy that came back from the form |
| `Services/EmployeeService.cs:120` | `employee.Department = null` before `Add` | Links by `DepartmentId` only, so EF doesn't try to insert the department again |

### Records, positional records and generics

| Where | What it does here | Why this choice |
|---|---|---|
| `Models/EmployeeQueryModels.cs:12-18` | `record EmployeeFilter(...)` with **default parameter values** | Immutable value-style input; callers set only what they need |
| `Components/Pages/Employees.razor:191-197` | Constructs the filter with **named arguments** | Readable, and safe against parameter-order mistakes |
| `Models/EmployeeQueryModels.cs:21-30` | **Generic record** `PagedResult<T>` with computed properties (`PageCount`, `FirstItemNumber`) | One paging result type serves both employees and departments |
| `Components/Shared/TablePager.razor:1, 37` | **Generic component** (`@typeparam T`) taking `PagedResult<T>` | One pager UI for any row type |
| `Models/EmployeeQueryModels.cs:32-51` | `EmployeeStats` and `DepartmentSummary` records | Read-only result shapes (DTOs) returned by the services |

### Nullable reference types and null operators

| Where | What it does here | Why this choice |
|---|---|---|
| `EmployeeManagementSystem.csproj:5` | `<Nullable>enable</Nullable>` | The compiler warns when something that may be null is used unchecked |
| `Models/Employee.cs:27`, `Models/Department.cs:14` | `Department? Department`, `string? Description` | Marks which values are legitimately optional |
| `Models/EmployeeQueryModels.cs:14, 18` | `int? DepartmentId`, `EmployeeStatus? Status` | `null` means "no filter / All" |
| `Services/EmployeeService.cs:22`, `DepartmentService.cs:65`, `EmployeeForm.razor:73` | `??=`, `??`, `?.` | Defaults for missing values without `if` blocks |
| `Components/Shared/TablePager.razor:37` | `= default!` (null-forgiving) | A required parameter that Blazor always sets; silences a warning that doesn't apply |

### Pattern matching and switch expressions

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/EmployeeService.cs:38, 43` | `if (filter.DepartmentId is int departmentId)` | Null check and unwrap in one step |
| `Services/EmployeeService.cs:58-64` | `switch` expression picks the sort order | Compact mapping from an enum to a query |
| `Helpers/Formatters.cs:9-15` | `switch` expression for status display text, with a `_` fallback | Exhaustive mapping in one expression |
| `Components/Pages/Departments.razor:134` | `d.EmployeeCount switch { 0 => …, 1 => …, var n => … }` | Different tooltip text for 0, 1 and many employees |
| `Services/EmployeeService.cs:133`, `Employees.razor:145` | `is null` / `is not null` | Null checks that can't be affected by overloaded `==` |

### Enums as result types

| Where | What it does here | Why this choice |
|---|---|---|
| `Models/EmployeeQueryModels.cs:38-43`, `DepartmentService.cs:114-141`, `Departments.razor:193` | `DepartmentDeleteResult { Deleted, NotFound, HasEmployees }`, returned by the service and switched on by the page | Three outcomes don't fit in a `bool`, and "has employees" is a normal case, not an exception |

### Attributes and data annotations validation

| Where | What it does here | Why this choice |
|---|---|---|
| `Models/Employee.cs:16-39`, `Department.cs:9-13`, `AppSettings.cs:28-33` | `[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]` with custom messages | Rules are written once on the model and enforced by the UI |
| `Components/Shared/EmployeeForm.razor:1-2, 76` | `EditForm` + `DataAnnotationsValidator`; `OnValidSubmit` runs only when every rule passes | Built-in Blazor validation, with no hand-written checks |
| `Models/Employee.cs:24` | `[Range(1, int.MaxValue)]` on `DepartmentId` | `0` means "not chosen", so this acts as a required dropdown |
| `Components/Shared/EmployeeForm.razor:65-70` | `[Parameter]`, `[EditorRequired]` | Marks component inputs; the compiler warns if a required one is missing |

### Delegates, events, lambdas and EventCallback

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/IAppSettingsService.cs:11`, `AppSettingsService.cs:19, 61` | `event Action? OnChange`, raised with `OnChange?.Invoke()` after a save | **Observer pattern**: the layout re-renders when settings change, without the service knowing about components |
| `Components/Layout/MainLayout.razor:102, 107-111, 113` | Subscribe (`+=`), handle with `InvokeAsync(StateHasChanged)`, unsubscribe (`-=`) | The event can fire on another circuit's thread; `InvokeAsync` moves the work onto this component's dispatcher |
| `Components/Pages/Settings.razor:107-114` | `SaveAsync(Action<AppSettings> change, string message)` takes a lambda describing the change | One save routine (clone, apply, persist, snackbar, error handling) shared by four settings |
| `Components/Shared/EmployeeForm.razor:69, 81` | `EventCallback<Employee> OnSubmit` | Child-to-parent communication that also triggers a re-render of the parent |
| `Data/DbSeeder.cs:29-30` | **Local function** `Employee New(...)` that captures `byName` | A small helper visible only inside the method that needs it |

### Blazor component model

| Where | What it does here | Why this choice |
|---|---|---|
| `Components/App.razor:17`, `Program.cs:10-11, 50-52` | Interactive Server render mode | No WebAssembly download; the UI runs next to the database |
| `Employees.razor:176`, `EditEmployee.razor:38`, `DepartmentHeadcountChart.razor:63` | Lifecycle hooks: `OnInitializedAsync`, `OnParametersSetAsync`, `OnParametersSet` | `OnParametersSet…` reruns when a route parameter changes (edit page) or new data arrives (chart) |
| `Components/Shared/EmployeeForm.razor:6` | `@bind-Value` **two-way binding** | Form fields write straight into the model |
| `Components/Shared/DepartmentHeadcountChart.razor:55-61` | A method returning a `RenderFragment` (Razor template) | The same chart markup is rendered with two option sets (wide and narrow screens) |
| `Components/Pages/Employees.razor:79-82` | `MudTable` with `MultiSelection` and `@bind-SelectedItems` into a `HashSet<Employee>` | Row checkboxes for bulk delete |

### ASP.NET Core middleware and configuration

| Where | What it does here | Why this choice |
|---|---|---|
| `Program.cs:39-45` | `UseHsts` (production only), `UseHttpsRedirection`, `UseAntiforgery` | Standard security pipeline; anti-forgery is required for Razor component endpoints |
| `Program.cs:48-52` | `MapStaticAssets`, `MapRazorComponents<App>()` | .NET 9+ static assets with fingerprinting (`@Assets[...]` in `App.razor:11-12`) |
| `Program.cs:18`, `appsettings.json` | `GetConnectionString("DefaultConnection")` | Connection details stay out of code and can be overridden per environment |

### Object copying, constants and immutability

| Where | What it does here | Why this choice |
|---|---|---|
| `Models/Employee.cs:54`, `Models/AppSettings.cs:43` | `Clone()` using `MemberwiseClone()` (a **shallow** copy) | Forms edit a copy, so cancelling never changes shared state. Settings pages clone the singleton's `Current` before editing. |
| `Services/AppSettingsService.cs:32, 60` | Save builds a new object and swaps `Current` as a whole | Readers on other circuits never see a half-updated settings object |
| `Models/AppSettings.cs:16-22` | `const` for scalars, `static readonly int[]` for `PageSizeOptions` | Arrays can't be `const`; `static readonly` gives one shared instance |

### Exceptions

| Where | What it does here | Why this choice |
|---|---|---|
| `Services/DepartmentService.cs:130-138` | `catch (DbUpdateException)`, mapped to `HasEmployees` | Covers the race where an employee is assigned between the check and the delete; the Restrict FK makes SQL refuse it |
| `Components/Shared/EmployeeForm.razor:79-86` | `try/finally` resets `_saving` | The Save button re-enables even if saving throws |
| `Components/Pages/Settings.razor:118` | `catch (Exception ex)`, shown as a snackbar | A failed save is reported instead of breaking the circuit |

---

## 8. Implementation walkthroughs

### Flow A — Create an employee

| Step | Layer | File / member | What happens |
|---|---|---|---|
| 1 | UI | `Components/Layout/NavMenu.razor:13` | The "Add Employee" link goes to `/employees/add` |
| 2 | Routing | `Components/Routes.razor:1-3` → `Components/Pages/AddEmployee.razor:1` | The router matches `@page "/employees/add"` and renders it inside `MainLayout` |
| 3 | UI → Service | `AddEmployee.razor:23-24` → `DepartmentService.GetAllAsync` (`DepartmentService.cs:16-20`) | Loads departments for the dropdown: `SELECT … FROM Departments ORDER BY Name` (no tracking) |
| 4 | UI | `Components/Shared/EmployeeForm.razor:1-60` | The user fills the form; `@bind-Value` writes into the `Employee` model; the department dropdown binds `DepartmentId` |
| 5 | Validation | `EmployeeForm.razor:2, 76` + `Models/Employee.cs:16-39` | On submit, `DataAnnotationsValidator` checks the attributes; only if all pass does `HandleValidSubmit` run |
| 6 | UI → parent | `EmployeeForm.razor:81` → `AddEmployee.razor:26` | `OnSubmit.InvokeAsync(Model)` calls the page's `SaveAsync` |
| 7 | Service | `EmployeeService.EmailExistsAsync` (`EmployeeService.cs:92-99`) | `SELECT CASE WHEN EXISTS(…) …` checks the email; if it's taken, a snackbar error appears and the flow stops |
| 8 | Service | `EmployeeService.AddAsync` (`EmployeeService.cs:116-126`) | Trims the text fields (`Normalize`), resets `Id`, clears the `Department` navigation, creates a context from the factory |
| 9 | Data | `db.Employees.Add` + `SaveChangesAsync` | EF Core sends an `INSERT INTO Employees …` and reads back the new identity `Id` |
| 10 | Database | `Data/AppDbContext.cs:52-61` constraints | SQL Server enforces the FK to `Departments` and the unique index on `Email` |
| 11 | Back to UI | `AddEmployee.razor:34-36` | A snackbar shows "<name> was added." and the page navigates to `/employees`, which reloads the list (Flow B) |

A reviewer may point out that steps 7 and 9 aren't atomic. If two people add the same email at the same moment, the
unique index rejects the second `INSERT` with a `DbUpdateException`, and `AddAsync` doesn't catch it.

### Flow B — Search, filter and page the employee list

| Step | Layer | File / member | What happens |
|---|---|---|---|
| 1 | UI | `Components/Pages/Employees.razor:176-181` | `OnInitializedAsync` takes the starting page size from the settings singleton (`Settings.Current.DefaultPageSize`), loads departments for the filter, then calls `LoadAsync` |
| 2 | UI | `Employees.razor:25-26` (search box, 300 ms debounce) → `OnSearchChanged` (`:203-208`) | Each filter change (search, department `:210`, status `:220`, sort `:227`, page size `:240`) stores the value, resets `_page = 1` and reloads |
| 3 | UI | `Employees.razor:185-201` (`LoadAsync`) | Clears the bulk-delete selection, builds an `EmployeeFilter` record with named arguments, and calls the service |
| 4 | Service | `EmployeeService.GetEmployeesAsync` (`EmployeeService.cs:20-46`) | Clamps the page size to `1..100`, then composes an `IQueryable` with `AsNoTracking`, `Include(Department)` and a `Where` for each filter that is set |
| 5 | Data | `EmployeeService.cs:49` | `CountAsync()` → `SELECT COUNT(*) … WHERE …`: the total for the *filtered* set |
| 6 | Service | `EmployeeService.cs:53-54` | Clamps the requested page into range, e.g. after the last row on the last page was deleted |
| 7 | Data | `EmployeeService.cs:58-69` | Sort with `Id` as the tie-breaker, then `Skip/Take` → `SELECT … ORDER BY … OFFSET @p ROWS FETCH NEXT @n ROWS ONLY`, joined to `Departments` |
| 8 | Service → UI | `EmployeeService.cs:71` → `Employees.razor:198` | Returns `PagedResult<Employee>`; the page adopts the possibly clamped `Page` |
| 9 | Data | `EmployeeService.GetStatsAsync` (`EmployeeService.cs:101-114`) | Separate count/average queries refresh the summary cards |
| 10 | UI | `Employees.razor:79-82` table + `Components/Shared/TablePager.razor` | The table renders `_result.Items` (dense if the setting says so); the pager shows "Showing x–y of n" and raises `PageChanged` / `PageSizeChanged` back to the page |

### Flow C — Change a setting and see it in every open tab

| Step | Layer | File / member | What happens |
|---|---|---|---|
| 1 | UI | `Components/Pages/Settings.razor:16` (dark-mode switch) → `OnDarkModeChanged` (`:83`) | The switch's `ValueChanged` calls `SaveAsync(s => s.DarkMode = value, …)` |
| 2 | UI | `Settings.razor:107-126` (`SaveAsync`) | Clones `SettingsService.Current`, applies the lambda to the clone, and calls the service. Failures become an error snackbar. |
| 3 | Service | `AppSettingsService.SaveAsync` (`AppSettingsService.cs:30-45`) | Clones again, trims the text, rejects an empty company name, and forces the page size into the allowed list |
| 4 | Data | `AppSettingsService.cs:47-58` | `FindAsync(1)` on a fresh context, then `SetValues` → `UPDATE AppSettings SET … WHERE Id = 1` |
| 5 | Service | `AppSettingsService.cs:60-61` | Swaps `Current` for the new object and raises `OnChange` |
| 6 | Every circuit | `Components/Layout/MainLayout.razor:102, 107-111` | Every open tab's layout is subscribed to the singleton's event. Each one queues `InvokeAsync` on its own circuit, copies `Current` and calls `StateHasChanged()`. |
| 7 | UI | `MainLayout.razor:6, 37-41` | `MudThemeProvider IsDarkMode` switches palettes; the app bar re-renders the company name and welcome message; `wwwroot/app.css` uses `var(--mud-palette-…)`, so custom styles follow |
| 8 | Next startup | `Program.cs:37` → `AppSettingsService.LoadAsync` (`:21-28`) | The saved row is loaded before the first request, so the saved theme shows without a light-mode flash |

Two settings work differently. Density and default page size are read when a page renders
(`Employees.razor:81, 178`), so they take effect on the next page load rather than live.
