# Employee Management System — Implementation Reference

A layer-by-layer reference for every part of the codebase. For a shorter tour, see [`overview.md`](overview.md).

**How to read this document**

- It is organised by **layer**, because the project follows a layered architecture (§1). The UI layer is then split by
  **feature**, because each feature is a group of pages.
- Each section covers six things: **Responsibility**, **Public surface**, **Flow**, **Dependencies**, **C# concepts**
  and **Errors & edge cases**.
- `path:line` citations refer to the working tree on top of commit `a9ef283`, including the uncommitted Settings,
  department-paging and documentation work. Line numbers drift as the code changes.
- Statements marked **⚠️ Inferred** come from reasoning about framework behaviour or intent, not from reading a line
  of this repository. Everything else was read directly from the code.

---

## Contents

1. [Architecture at a glance](#1-architecture-at-a-glance)
2. [Hosting and composition root](#2-hosting-and-composition-root)
3. [Domain models](#3-domain-models)
4. [Data access](#4-data-access)
5. [Services](#5-services)
6. [UI layer](#6-ui-layer)
   - 6.1 [App shell (host page, router, layout, styles)](#61-app-shell)
   - 6.2 [Employees feature](#62-employees-feature)
   - 6.3 [Departments feature](#63-departments-feature)
   - 6.4 [Dashboard and Reports](#64-dashboard-and-reports)
   - 6.5 [Settings feature](#65-settings-feature)
   - 6.6 [Shared components and helpers](#66-shared-components-and-helpers)
7. [Cross-cutting concerns](#7-cross-cutting-concerns)
8. [Extension points](#8-extension-points)
9. [Review notes (consolidated)](#9-review-notes-consolidated)

---

## 1. Architecture at a glance

```
Browser ──SignalR circuit──► Razor components (Components/)
                                   │  @inject I…Service
                                   ▼
                             Services (Services/)  ── business rules, paging, validation
                                   │  IDbContextFactory<AppDbContext>
                                   ▼
                             AppDbContext (Data/)  ── EF Core model, migrations (Migrations/)
                                   │
                                   ▼
                             SQL Server (EmployeeManagementDb)

               Models/ (entities, enums, records) are shared by every layer.
```

- **One project, one process.** Blazor Web App with the **Interactive Server** render mode: components run on the
  server, and the browser exchanges events and DOM diffs with it over a SignalR connection (`Components/App.razor:17`).
- **Dependencies point one way:** UI → Services → Data. Pages never touch `AppDbContext`; every page injects service
  interfaces (for example `Components/Pages/Employees.razor:2-4`).
- **Not Clean Architecture:** the services depend directly on EF Core (`Services/EmployeeService.cs:3`), and the EF
  entities double as UI form models (`Components/Shared/EmployeeForm.razor:65`). There are no repositories, DTO
  mappers or separate projects.

---

## 2. Hosting and composition root

**Files:** `Program.cs`, `EmployeeManagementSystem.csproj`, `appsettings.json`, `Properties/launchSettings.json`

### Responsibility
- **Owns:** building the host, registering every service with its lifetime, preparing the database at startup
  (migrate and seed), loading settings, the HTTP middleware pipeline, and mapping the Blazor endpoint.
- **Does not own:** any business rule or UI. It knows that services exist but not what they do. There are no API
  endpoints, controllers, hosted services or authentication.

### Public surface
| Member | Where | Contract |
|---|---|---|
| Top-level statements (compiler-generated `Main`) | `Program.cs:1-54` | Process entry point |
| `AddDbContextFactory<AppDbContext>` | `Program.cs:17-18` | Provides `IDbContextFactory<AppDbContext>` (registered as a singleton by EF Core ⚠️ Inferred from EF Core defaults) |
| `IEmployeeService` → `EmployeeService` (scoped) | `Program.cs:21` | One instance per circuit |
| `IDepartmentService` → `DepartmentService` (scoped) | `Program.cs:22` | One instance per circuit |
| `IAppSettingsService` → `AppSettingsService` (singleton) | `Program.cs:23` | One instance for the whole process |
| `ConnectionStrings:DefaultConnection` | `appsettings.json` | SQL Server, Windows auth, `TrustServerCertificate=True` |
| Launch profiles `https` / `http` | `Properties/launchSettings.json` | `https://localhost:7100;http://localhost:5100`, `ASPNETCORE_ENVIRONMENT=Development` |

### Flow
1. `WebApplication.CreateBuilder(args)` loads configuration and logging and configures Kestrel (`Program.cs:7`).
2. DI registrations: Blazor (`:10-11`), MudBlazor (`:14`), EF Core factory (`:17-18`), application services (`:21-23`).
3. `builder.Build()` (`:25`).
4. A temporary scope resolves the context factory, runs `Database.MigrateAsync()`, then `DbSeeder.SeedAsync(db)`
   (`:28-34`).
5. The singleton settings service loads its row (`:37`).
6. Middleware: `UseHsts` outside Development (`:39-42`), `UseHttpsRedirection` (`:44`), `UseAntiforgery` (`:45`).
7. Endpoints: `MapStaticAssets` (`:48`), `MapRazorComponents<App>().WithStaticAssets().AddInteractiveServerRenderMode()`
   (`:50-52`).
8. `app.Run()` blocks and serves requests (`:54`).

### Dependencies
- **Uses:** `EmployeeManagementSystem.Components` (`App`), `.Data` (`AppDbContext`, `DbSeeder`), `.Services`, EF Core,
  MudBlazor (`Program.cs:1-5`).
- **Used by:** the .NET host only.

### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Top-level statements | `Program.cs:7-54` | No `Program` class or `Main` written by hand | Minimal-hosting style of modern ASP.NET Core templates |
| Top-level `await` | `Program.cs:31-33, 37` | Startup awaits migration, seeding and settings load | Async startup without `.Result` or `.Wait()`, which could deadlock |
| Extension-method registration | `Program.cs:10, 14, 17` | `AddRazorComponents`, `AddMudServices`, `AddDbContextFactory` | Library-provided "register everything I need" helpers |
| DI lifetimes (scoped vs singleton) | `Program.cs:21-23` | Per-circuit services; one app-wide settings service | Settings are global and must broadcast to all tabs (see §5.3) |
| `using` statement on a DI scope | `Program.cs:28` | Creates and disposes a scope outside any request | There is no request scope at startup |
| `await using` | `Program.cs:31` | Asynchronously disposes the startup `DbContext` | `DbContext` implements `IAsyncDisposable` |
| Generic method with a type argument | `Program.cs:50` | `MapRazorComponents<App>()` names the root component | Compile-time-checked root type |
| Implicit usings | `.csproj:6` | `System`, `System.Linq` and similar are always available | Less boilerplate |
| Nullable reference types | `.csproj:5` | Project-wide null-safety warnings | Catches null bugs at compile time |

### Errors and edge cases
- **Database unreachable at startup:** `MigrateAsync` (`:32`) throws and the process exits before serving any request.
  There is no retry or try/catch.
- **Pending model changes:** EF Core 9+ makes `MigrateAsync` throw `PendingModelChangesWarning` as an error if the
  model differs from the last migration. The project hit this when the app was run without rebuilding
  (⚠️ Inferred as a general rule; observed once during development).
- **Seeding is skipped** if *either* table has rows (`Data/DbSeeder.cs:10`). Deleting all employees but keeping
  departments will not re-seed.
- **HTTP vs HTTPS:** with the `http` profile, `UseHttpsRedirection` has no HTTPS port and logs
  "Failed to determine the https port for redirect", then serves plain HTTP (observed in the run log).

---

## 3. Domain models

**Files:** `Models/Employee.cs`, `Models/Department.cs`, `Models/AppSettings.cs`, `Models/EmployeeQueryModels.cs`

### Responsibility
- **Owns:** the shape of the data (entities), enum vocabularies, the default and allowed values for settings,
  **validation rules as attributes**, and the request/result records passed between UI and services.
- **Does not own:** persistence mapping (that lives in `AppDbContext`, §4) or behaviour beyond small computed
  properties and `Clone()`.

### Public surface

**Entities**
| Type | Members | Contract |
|---|---|---|
| `Employee` (`Employee.cs:12-55`) | `Id`; `FirstName`, `LastName` (required, ≤50); `DepartmentId` (≥1); `Department?` (navigation); `Email` (required, email format); `Salary` (1…10,000,000); `JobTitle` (required, ≤80); `Status`; `DateHired?` (default today); computed `FullName` (`:46`), `Initials` (`:48-51`); `Clone()` (`:54`) | Also used directly as the form model |
| `EmployeeStatus` (`Employee.cs:5-10`) | `Active`, `OnLeave`, `Inactive` | Stored as text (§4) |
| `Department` (`Department.cs:5-17`) | `Id`; `Name` (required, ≤100); `Description?` (≤500); `Employees` collection | Parent of `Employee` |
| `AppSettings` (`AppSettings.cs:14-44`) | `Id` (always 1); `DarkMode`; `CompanyName` (required, ≤100); `WelcomeMessage` (≤100, may be empty); `DefaultPageSize`; `TableDensity`; computed `IsCompact` (`:41`); `Clone()` (`:43`) | Single-row table |
| `TableDensity` (`AppSettings.cs:5-9`) | `Comfortable`, `Compact` | |
| Constants (`AppSettings.cs:16-22`) | `SingletonId = 1`, `DefaultCompanyName`, `DefaultWelcomeMessage`, `DefaultPageSizeValue = 10`, `PageSizeOptions = {5,10,25,50}` | The single source of defaults; used by the DbContext seed data, the service and the pager |

**Query and result records** (`EmployeeQueryModels.cs`)
| Type | Lines | Contract |
|---|---|---|
| `EmployeeSortOption` | `3-9` | `NameAsc` (default), `NameDesc`, `SalaryAsc`, `SalaryDesc` |
| `EmployeeFilter(Search?, DepartmentId?, Sort, Page = 1, PageSize = 10, Status?)` | `12-18` | `null` means "don't filter". `Status` is the last parameter; it was added after the others, and putting it last kept existing positional calls compiling (from the change history, not visible in the code) |
| `PagedResult<T>(Items, TotalCount, Page, PageSize)` | `21-30` | `PageCount` (`:27`, at least 1), `FirstItemNumber` (`:28`, 0 when empty), `LastItemNumber` (`:29`) |
| `EmployeeStats(TotalCount, AverageSalary, OnLeaveCount, DepartmentCount)` | `32-36` | Dashboard and Employees cards |
| `DepartmentDeleteResult` | `38-43` | `Deleted`, `NotFound`, `HasEmployees` |
| `DepartmentSummary(Id, Name, Description?, EmployeeCount, AverageSalary, TotalPayroll)` | `45-51` | Read model for the Departments page, Reports and charts |

### Flow
Models carry no flow of their own. They are created by EF Core when reading (§4), by pages for new records
(`AddEmployee.razor:20`), and by services for results (`EmployeeService.cs:71`). Attribute rules run when a form's
`DataAnnotationsValidator` validates the bound model (§7, Validation).

### Dependencies
- **Uses:** `System.ComponentModel.DataAnnotations` only (`Employee.cs:1`, `Department.cs:1`, `AppSettings.cs:1`).
- **Used by:** every other layer.

### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Data-annotation attributes | `Employee.cs:16-39`, `Department.cs:9-13`, `AppSettings.cs:28-33` | Declare validation rules and user-facing messages | Rules are written once and enforced by Blazor's `DataAnnotationsValidator` |
| `[Range(1, int.MaxValue)]` as "required" | `Employee.cs:24` | Rejects `DepartmentId == 0` ("not chosen") | `[Required]` can't reject a non-nullable `int` with value 0 |
| Positional records with defaults | `EmployeeQueryModels.cs:12-18` | Immutable filter where callers set only what they need | Value semantics, concise, works with named arguments |
| Generic record with computed members | `EmployeeQueryModels.cs:21-30` | One paging envelope for any row type | Reused by the employee and department paging |
| Nullable value types as "no filter" | `EmployeeQueryModels.cs:14, 18` | `int?` / `EmployeeStatus?` where `null` means "All" | No sentinel values needed |
| Nullable reference annotations | `Employee.cs:27`, `Department.cs:14` | Optional navigation and description | Compiler-checked optionality |
| Expression-bodied computed properties | `Employee.cs:46-51`, `AppSettings.cs:41` | `FullName`, `Initials`, `IsCompact` | Derived values that EF Core doesn't map ⚠️ Inferred: EF ignores get-only properties by convention |
| `MemberwiseClone` (shallow copy) | `Employee.cs:54`, `AppSettings.cs:43` | Copy before editing | Editing a copy keeps the shared or saved instance unchanged until save |
| `const` vs `static readonly` | `AppSettings.cs:16-22` | Scalars are `const`; the array is `static readonly` | Arrays can't be `const` |
| Enums | `Employee.cs:5`, `AppSettings.cs:5`, `EmployeeQueryModels.cs:3, 38` | Closed vocabularies, including a result enum | Type-safe choices; three-way delete outcome |
| Digit separators | `Employee.cs:33` | `10_000_000` | Readability |

### Errors and edge cases
- `Clone()` is **shallow**: a cloned `Employee` shares its `Department` reference (`Employee.cs:54`). None of the
  callers modify the navigation, so this is harmless today.
- `AppSettings.PageSizeOptions` is a **public mutable array** (`AppSettings.cs:22`); any code could overwrite an
  element. Nothing does.
- `[EmailAddress]` is a loose format check; the definitive uniqueness check is the database index (§4).
- `Initials` handles empty names by using a space and then `Trim()` (`Employee.cs:48-51`).
- `PagedResult.PageCount` returns 1 for an empty result (`:27`), so the pager always shows one page.

---

## 4. Data access

**Files:** `Data/AppDbContext.cs`, `Data/DbSeeder.cs`, `Migrations/*`

### Responsibility
- **Owns:** the mapping between entities and SQL tables: keys, lengths, precision, enum storage, unique indexes,
  the FK and its delete rule, the seed row for settings, the sample data, and the migration history.
- **Does not own:** queries (those are in the services) or validation messages (those are in the models).

### Public surface
| Member | Where | Contract |
|---|---|---|
| `AppDbContext(DbContextOptions<AppDbContext>)` | `AppDbContext.cs:8-10` | Options come from DI (`Program.cs:17-18`) |
| `DbSet<Employee> Employees` | `:12` | |
| `DbSet<Department> Departments` | `:13` | |
| `DbSet<AppSettings> AppSettings` | `:14` | |
| `OnModelCreating(ModelBuilder)` | `:16-63` | Fluent configuration (below) |
| `DbSeeder.SeedAsync(AppDbContext)` | `DbSeeder.cs:8` | Idempotent: does nothing if data exists |

**Schema rules** (`AppDbContext.cs`)
| Table | Rule | Lines |
|---|---|---|
| `AppSettings` | PK `Id`, never generated; `CompanyName` NOT NULL ≤100; `WelcomeMessage` NOT NULL ≤100 (may be `''`); `TableDensity` stored as `nvarchar(20)`; seed row `Id = 1` | `18-36` |
| `Departments` | `Name` NOT NULL ≤100, **unique index**; `Description` ≤500 | `38-44` |
| `Employees` | Names ≤50; **required FK `DepartmentId` with `ON DELETE RESTRICT`** (`52-56`); `JobTitle` ≤80; `Email` ≤200 with **unique index** (`58, 61`); `Salary decimal(18,2)` (`59`); `Status` as `nvarchar(20)` (`60`) | `46-62` |

**Migrations**
| Migration | Effect |
|---|---|
| `20261101103513_InitialCreate` | Creates `Employees` (with a text `Department` column) and `IX_Employees_Email` |
| `20261102212854_AddDepartments` | **Drops** `Employees.Department`, adds `DepartmentId`, creates `Departments`, `IX_Departments_Name`, `IX_Employees_DepartmentId`, and the FK. Destructive: the database was reset when it was applied. |
| `20261102224948_AddAppSettings` | Creates `AppSettings` and inserts the default row |
| `20261102225922_AddWelcomeMessage` | Adds `WelcomeMessage` (NOT NULL, default `''`), then sets row 1 to "Welcome back" |

### Flow
- **Model building:** EF Core calls `OnModelCreating` once per process and caches the model ⚠️ Inferred (EF Core
  default).
- **Startup:** `MigrateAsync` applies pending migrations, then `SeedAsync` runs (`Program.cs:32-33`).
- **Seeding** (`DbSeeder.cs`):
  1. Return early if any department or employee exists (`:10-13`).
  2. Insert 7 departments and save, so they get identity IDs (`:15-26`).
  3. Build a name → entity dictionary (`:28`).
  4. Create 8 employees through a local function that looks up the department ID (`:29-40`).
  5. Save (`:42`).

### Dependencies
- **Uses:** EF Core (`Microsoft.EntityFrameworkCore`), the models.
- **Used by:** every service (through `IDbContextFactory<AppDbContext>`) and `Program.cs` (startup).

### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| `DbContext` subclass + constructor injection of options | `AppDbContext.cs:6-10` | The configured unit of work | The standard EF Core pattern; options supplied by DI |
| Expression-bodied `DbSet` properties | `:12-14` | `=> Set<T>()` instead of auto-properties | Avoids nullable warnings on uninitialised `DbSet` properties ⚠️ Inferred motivation |
| Fluent API with lambdas | `:16-62` | `entity.Property(e => e.Email).HasMaxLength(200)` | Strongly typed configuration kept out of the entities |
| Generic value conversion | `:24, 60` | `HasConversion<string>()` stores enums as names | Readable rows; safe if enum members are reordered |
| Relationship configuration | `:52-56` | `HasOne/WithMany/HasForeignKey/IsRequired/OnDelete(Restrict)` | The database rejects deleting a non-empty department |
| Seed data (`HasData`) | `:27-35` | The migration inserts the single settings row | Guarantees the row exists without runtime insert logic |
| Qualified name to avoid a clash | `:29-33` | `Models.AppSettings.SingletonId` | Inside the context, `AppSettings` refers to the `DbSet` property, not the type |
| Static class | `DbSeeder.cs:6` | Stateless helper | Never instantiated |
| Local function capturing a variable | `DbSeeder.cs:29-30` | `New(...)` closes over `byName` | Keeps the seed list short and readable |
| Collection initialisers, `ToDictionary` (LINQ) | `DbSeeder.cs:15-28` | Build seed data and the lookup | Concise |
| `Random.Shared` | `DbSeeder.cs:54` | Random hire dates | Thread-safe shared instance (.NET 6+) |
| String interpolation + `ToLowerInvariant` | `DbSeeder.cs:53` | Builds email addresses | Culture-independent lower-casing |

### Errors and edge cases
- **Restrict FK** (`:56`): deleting a department with employees throws `DbUpdateException`. The service catches it
  (§5.2).
- **Unique indexes** (`:43, 61`): duplicate names or emails throw `DbUpdateException` on save. The services check
  first, but don't catch a race (§9).
- **Case sensitivity:** uniqueness and `==` comparisons follow the database collation. SQL Server's default is
  case-insensitive, so "it" collides with "IT". This was confirmed during development: inserting "it" was rejected
  by the unique index. The collation itself is ⚠️ Inferred to be the default.
- **`AddDepartments` is destructive** for existing data. Any database still on `InitialCreate` would lose its
  department text.

---

## 5. Services

Shared pattern for all three services:
- Each one implements an interface (`I…Service`).
- Each takes `IDbContextFactory<AppDbContext>` in its constructor.
- Each method creates a short-lived context with `await using var db = await _factory.CreateDbContextAsync();`.
- Read-only queries use `AsNoTracking()`.

### 5.1 EmployeeService

**Files:** `Services/IEmployeeService.cs`, `Services/EmployeeService.cs`

#### Responsibility
- **Owns:** querying employees (search, filter by department and status, sort, server-side paging), reading one or
  the most recent, create, update, single and bulk delete, the email uniqueness check, and dashboard statistics.
  It also normalises text by trimming it.
- **Does not own:** validation messages or format rules (those are model attributes), user feedback, or navigation.
  Its callers decide what to do with a `false` or `0` result.

#### Public surface (`IEmployeeService.cs`)
| Signature | Line | Contract |
|---|---|---|
| `Task<PagedResult<Employee>> GetEmployeesAsync(EmployeeFilter? filter = null)` | `8` | Filters, sorts and pages. `PageSize` is clamped to 1…`MaxPageSize` (100); `Page` is clamped to the existing range; `Department` is included |
| `Task<IReadOnlyList<Employee>> GetRecentAsync(int count)` | `9` | Newest first, by `Id` |
| `Task<Employee?> GetByIdAsync(int id)` | `10` | `null` if not found; includes `Department` |
| `Task<Employee> AddAsync(Employee employee)` | `11` | Mutates the argument: trims, sets `Id = 0`, clears the navigation. Returns it with the new `Id` |
| `Task<bool> UpdateAsync(Employee employee)` | `12` | `false` if the row no longer exists |
| `Task<bool> DeleteAsync(int id)` | `13` | `true` if a row was deleted |
| `Task<int> DeleteManyAsync(IEnumerable<int> ids)` | `16` | Number of rows actually deleted; empty input → 0 without touching the DB |
| `Task<bool> EmailExistsAsync(string email, int? excludeEmployeeId = null)` | `18` | Trimmed exact match (collation-dependent); excludes the employee being edited |
| `Task<EmployeeStats> GetStatsAsync()` | `19` | Totals across all employees (ignores filters) |
| `const int MaxPageSize = 100` | `EmployeeService.cs:18` | Also used by `DepartmentService` |

#### Flow — `GetEmployeesAsync` (`EmployeeService.cs:20-72`)
1. Default the filter (`:22`) and clamp the page size (`:23`).
2. Start `Employees.AsNoTracking().Include(Department)` (`:28`).
3. Add `Where` clauses for each filter that is set: search on "First Last" or email with `Contains` (`:30-36`),
   department (`:38-41`), status (`:43-46`).
4. `CountAsync` over the filtered query (`:49`).
5. Compute the page count and clamp the requested page (`:53-54`).
6. Pick the sort with a `switch` expression, always ending with `ThenBy(Id)` (`:58-64`).
7. `Skip/Take` → `ToListAsync` (`:66-69`).
8. Return `PagedResult` with the effective page (`:71`).

#### Flow — writes
- **Add** (`:116-126`): normalise → reset `Id` and the navigation → `Add` → `SaveChangesAsync` (INSERT; identity is
  read back).
- **Update** (`:128-142`): `FindAsync(Id)` → `null` means return `false` → normalise → `SetValues` copies the scalar
  properties onto the tracked entity → `SaveChangesAsync` (UPDATE of changed columns ⚠️ Inferred: EF only updates
  modified properties).
- **Delete** (`:144-149`) and **DeleteMany** (`:151-162`): `ExecuteDeleteAsync` sends `DELETE … WHERE Id = @id` or
  `WHERE Id IN (…)` without loading any rows.

#### Dependencies
- **Uses:** `IDbContextFactory<AppDbContext>`, models, EF Core.
- **Used by:** `Employees.razor`, `AddEmployee.razor`, `EditEmployee.razor`, `EmployeeDetails.razor`, `Home.razor`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Interface implementation | `EmployeeService.cs:7` | Pages depend on `IEmployeeService` | Swappable implementation; the project history shows an earlier in-memory version was replaced without changing pages (commit `25d6be2` removed that doc comment) |
| Constructor injection + `readonly` field | `:9-14` | Receives the context factory | Explicit dependency, can't be reassigned |
| Context factory | `:25` and every method | One context per operation | Blazor Server circuits are long-lived and `DbContext` is not thread-safe; an earlier comment said "a long-lived DbContext is unsafe" (commit `25d6be2`) |
| `async`/`await` + `Task<T>` | every method | Non-blocking database I/O | Server threads are shared by all users |
| `await using` | `:25, 76, 86, …` | Async disposal of each context | Returns the connection promptly |
| Null-coalescing assignment `??=` | `:22` | `filter ??= new EmployeeFilter()` | Optional argument with a default |
| `Math.Clamp` | `:23, 54` | Bounds page size and page | Defends against bad input or stale pages |
| `IQueryable` composition (deferred execution) | `:28-46` | Conditionally adds `Where` | One SQL statement, built from the filters that are set |
| Type pattern with declaration | `:38, 43` | `filter.DepartmentId is int departmentId` | Null check and unwrap in one step |
| `switch` expression | `:58-64` | Maps the enum to an ordered query | Concise and exhaustive with `_` |
| Deterministic ordering for paging | `:60-63` | `ThenBy(e => e.Id)` | Stable `Skip/Take` when values tie (comment `:56-57`) |
| `AsNoTracking`, `Include` | `:28, 77-78, 87-88` | Read-only queries with eager loading | Performance; the department name in one query |
| Nullable aggregate trick | `:108` | `Select(e => (decimal?)e.Salary).AverageAsync() ?? 0m` | `Average` over an empty set returns `null` instead of throwing |
| Change-tracker update | `:132-139` | `FindAsync` + `Entry(existing).CurrentValues.SetValues(employee)` | Updates from a detached object without attaching it |
| Bulk operations | `:147, 161` | `ExecuteDeleteAsync` | Set-based SQL; bulk delete is one statement (comment `:159`) |
| LINQ `Distinct`/`Contains` → SQL `IN` | `:153, 161` | Deduplicates IDs, then filters | Translates to a parameterised `IN` list ⚠️ Inferred SQL shape |
| Private static helper | `:164-170` | `Normalize` trims text fields | No instance state needed |

#### Errors and edge cases
- **Stale page** (for example after deleting the last row of the last page): the page is clamped (`:53-54`). Callers
  adopt the returned `Page` (`Employees.razor:198`).
- **No rows:** `PageCount` 1, `Items` empty; stats average 0 (`:108`).
- **Duplicate email race:** callers check `EmailExistsAsync` first, but if two saves race, the unique index throws
  `DbUpdateException` from `AddAsync` or `UpdateAsync`. **Not caught** anywhere (see §7, Error handling).
- **Department deleted while an employee form was open:** the FK violation on `SaveChangesAsync` is **not caught**.
- **Update of a deleted employee:** returns `false` (`:133-136`).
- **`AddAsync` and `UpdateAsync` mutate the caller's object** (`Normalize`, `Id = 0`, `Department = null`). Callers
  don't reuse the object afterwards, so this is harmless.
- **Bulk delete partially applied by someone else:** returns the number actually deleted; the page reports
  "N of M" (`Employees.razor:271-275`).
- **Search** uses `Contains`, which becomes SQL `LIKE '%term%'` ⚠️ Inferred. It can't use an index, which is fine at
  this scale; case sensitivity follows the collation.

### 5.2 DepartmentService

**Files:** `Services/IDepartmentService.cs`, `Services/DepartmentService.cs`

#### Responsibility
- **Owns:** department list, read models with aggregates (count, average, payroll) both full and paged, the name
  uniqueness check, create and rename, and **the "no delete while employees exist" rule**.
- **Does not own:** employee data, or how the summaries are presented.

#### Public surface (`IDepartmentService.cs`)
| Signature | Line | Contract |
|---|---|---|
| `Task<IReadOnlyList<Department>> GetAllAsync()` | `7` | All departments, ordered by name (for dropdowns) |
| `Task<IReadOnlyList<DepartmentSummary>> GetSummariesAsync()` | `9` | Every department, including empty ones, with aggregates (for charts and reports) |
| `Task<PagedResult<DepartmentSummary>> GetSummaryPageAsync(int page, int pageSize)` | `12` | Ordered by `Name, Id`; page size clamped to 1…100; page clamped |
| `Task<Department?> GetByIdAsync(int id)` | `13` | |
| `Task<bool> NameExistsAsync(string name, int? excludeDepartmentId = null)` | `15` | Trimmed match; excludes the department being edited |
| `Task<Department> AddAsync(Department department)` | `16` | Builds a new entity from the trimmed input; returns it with its `Id` |
| `Task<bool> UpdateAsync(Department department)` | `17` | `false` if not found |
| `Task<DepartmentDeleteResult> DeleteAsync(int id)` | `20` | `NotFound`, `HasEmployees` or `Deleted` |

#### Flow — summaries (`DepartmentService.cs:22-67`)
1. `GetSummariesAsync` / `GetSummaryPageAsync` build an ordered (and, for paging, `Skip/Take`) `IQueryable<Department>`
   (`:25`, `:40-44`).
2. `ToSummariesAsync` projects to an anonymous type with `Count()`, `Average((decimal?)…)` and `Sum((decimal?)…)` over
   `d.Employees`. This runs in SQL as correlated aggregates ⚠️ Inferred SQL shape (`:52-62`).
3. The rows are mapped in memory to `DepartmentSummary`, turning `null` aggregates into 0 for empty departments
   (`:64-66`).

#### Flow — delete (`:114-141`)
1. `FindAsync(id)`; if missing, return `NotFound` (`:118-122`).
2. If any employee has this `DepartmentId`, return `HasEmployees` (`:124-127`).
3. `Remove` + `SaveChangesAsync` in `try`; a `DbUpdateException` means `HasEmployees` (`:129-138`).
4. Otherwise return `Deleted` (`:140`).

#### Dependencies
- **Uses:** `IDbContextFactory<AppDbContext>`, models, EF Core, `EmployeeService.MaxPageSize` (`:30`).
- **Used by:** `Departments.razor`, `Employees.razor`, `AddEmployee.razor`, `EditEmployee.razor`, `Home.razor`,
  `Reports.razor`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Anonymous-type projection | `:52-62` | Shapes aggregates for SQL translation | Commit `4c3083c` made this query EF-translatable; the record is built after materialisation |
| Casting to nullable for aggregates | `:59-60` | `(decimal?)e.Salary` | Empty departments produce `null` instead of an exception |
| Null-coalescing `??` | `:65` | `x.Average ?? 0m` | Zero for empty departments |
| Private static async helper taking `IQueryable` | `:50` | One projection shared by the full and paged queries | The full and paged numbers can't drift apart (comment `:49`) |
| Result enum instead of exceptions | `:114-141` | Returns `DepartmentDeleteResult` | "Has employees" is an expected outcome, not an exceptional one |
| `try/catch` on a specific exception | `:130-138` | Catches `DbUpdateException` from the Restrict FK | Covers the race between the check and the delete (comment `:136`) |
| Object initialiser for a fresh entity | `:86-90` | `AddAsync` builds a new `Department` | Never inserts the caller's object (unlike `EmployeeService.AddAsync`) |
| Expression-bodied static helper + `string?` | `:143-144` | Blank description becomes `null` | Stores NULL instead of `''` or whitespace |

#### Errors and edge cases
- **Delete races** are handled (`:134-138`); not found returns `NotFound`.
- **Rename to an existing name:** the page checks `NameExistsAsync` first (`Departments.razor:158`). In a race, the
  unique index throws `DbUpdateException` from `AddAsync`/`UpdateAsync`, which is **not caught**.
- **Coupling:** `GetSummaryPageAsync` borrows `EmployeeService.MaxPageSize` (`:30`), a cross-service dependency on a
  constant.
- `GetAllAsync` orders by `Name` only (`:19`); ties are impossible because names are unique.

### 5.3 AppSettingsService

**Files:** `Services/IAppSettingsService.cs`, `Services/AppSettingsService.cs`

#### Responsibility
- **Owns:** the in-memory copy of the single settings row, loading it, validating and saving changes, and
  **notifying subscribers** after a save.
- **Does not own:** applying settings. Each consumer reads `Current` (the layout for theme and name; pages for
  density and page size).

#### Public surface (`IAppSettingsService.cs`)
| Member | Line | Contract |
|---|---|---|
| `AppSettings Current { get; }` | `8` | Treat as read-only; edit a `Clone()` and call `SaveAsync` |
| `event Action? OnChange` | `11` | Raised after a successful save, **on the saving thread**; component handlers must use `InvokeAsync` |
| `Task LoadAsync()` | `14` | Called once at startup (`Program.cs:37`) |
| `Task SaveAsync(AppSettings settings)` | `17` | Trims text; **throws `ArgumentException` if `CompanyName` is empty**; replaces an invalid page size with 10; upserts row 1; swaps `Current`; raises `OnChange` |

#### Flow — `SaveAsync` (`AppSettingsService.cs:30-62`)
1. Clone the input and force `Id = 1` (`:32-33`).
2. Trim the text, treating a `null` welcome message as empty (`:34-35`).
3. Reject an empty company name (`:37-40`); fix an invalid page size (`:42-45`).
4. Fresh context: `FindAsync(1)`; `Add` if missing, else `SetValues` (`:47-56`). `SaveChangesAsync` (`:58`).
5. `Current = clean` (`:60`), then `OnChange?.Invoke()` (`:61`).

#### Dependencies
- **Uses:** `IDbContextFactory<AppDbContext>`, `AppSettings`.
- **Used by:** `Program.cs` (load), `MainLayout.razor` (theme, name, subscription), `Settings.razor` (edit),
  `Employees.razor` / `Departments.razor` / `Reports.razor` (density, default page size).

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Singleton lifetime | `Program.cs:23` | One shared instance | Company-wide settings; a save must reach every circuit |
| Events and delegates (observer) | `IAppSettingsService.cs:11`, `AppSettingsService.cs:19, 61` | `event Action?` raised with `?.Invoke()` | Decouples the service from the components that react |
| Null-conditional invoke | `:61` | `OnChange?.Invoke()` | No subscribers → no-op, no null check |
| Immutable-by-replacement state | `:60` | Swaps the whole `Current` object instead of mutating it | Readers on other threads never see a half-updated object |
| Private setter | `:17` | `Current { get; private set; }` | Only the service can replace it |
| Exception for invalid input | `:39` | `ArgumentException` with `nameof(settings)` | A programming-contract violation; the page catches and displays it |
| Upsert via `FindAsync` + `Add`/`SetValues` | `:48-56` | Works even if the seed row is missing | Defensive against a manually emptied table |
| `?? new()` fallback | `:27` | Defaults if no row exists | The app still renders |

#### Errors and edge cases
- **Concurrent saves** from two tabs: last write wins. There is no concurrency token, and each save starts from that
  page's view of `Current` (`Settings.razor:112`). ⚠️ Inferred: two simultaneous saves could drop one of the changes.
- **Database down during save:** the exception propagates to `Settings.razor`, which shows an error snackbar
  (`Settings.razor:118-121`). `Current` is unchanged, because it is set only after a successful save (`:58-60`).
- **Subscriber exceptions:** handlers run synchronously inside `Invoke()`. `MainLayout`'s handler only queues
  `InvokeAsync` and discards the task, so its exceptions are unobserved ⚠️ Inferred.
- **Leaks:** every subscriber must unsubscribe. `MainLayout` does (`MainLayout.razor:113`); any new subscriber must
  do the same.
- **Startup load failure** stops the process before serving any request (`Program.cs:37`).

---

## 6. UI layer

Shared mechanics:
- **Routing:** each page declares `@page` and is rendered inside `MainLayout`.
- **Dependencies:** pages get services through `@inject`.
- **State:** each page keeps its state in private fields and reloads through a `LoadAsync`-style method.
- **Feedback:** user messages go through MudBlazor's `ISnackbar`.
- **Confirmation:** destructive actions go through `ConfirmDialog` (an overlay controlled by a parameter, not the
  MudBlazor dialog service).

### 6.1 App shell

**Files:** `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor`,
`Components/Layout/MainLayout.razor`, `Components/Layout/NavMenu.razor`, `wwwroot/app.css`

#### Responsibility
- **Owns:** the HTML document, the render mode, routing and the 404 page, the theme (light and dark palettes), the
  app bar (company name and welcome message), the drawer and navigation, and global CSS.
- **Does not own:** page content, or any data other than settings.

#### Public surface
| Item | Where | Contract |
|---|---|---|
| Render mode | `App.razor:13, 17` | `HeadOutlet` and `Routes` are `InteractiveServer`: the whole app is interactive over SignalR |
| Assets | `App.razor:11-12, 19` | MudBlazor CSS/JS and `app.css` through `@Assets[...]` (fingerprinted) |
| Router | `Routes.razor:1-12` | Scans the assembly for `@page`; default layout `MainLayout`; `NotFound` renders a message inside the layout |
| Global usings | `_Imports.razor:1-16` | Every `.razor` file sees MudBlazor, Models, Services, Helpers and Shared |
| `MainLayout` | `MainLayout.razor:1-119` | `@inherits LayoutComponentBase`, `@implements IDisposable`; renders `@Body` (`:48`) |
| Theme | `MainLayout.razor:57-96` | `PaletteLight` / `PaletteDark`, drawer width, radius, Inter font |
| Navigation | `NavMenu.razor:2-32` | Links to `/`, `/employees`, `/employees/add`, `/departments`, `/reports`, `/settings` |

#### Flow
1. `App.razor` is served. `blazor.web.js` starts the circuit (`App.razor:18`).
2. `Routes` resolves the page. `MainLayout.OnInitialized` copies `Settings.Current` and subscribes to `OnChange`
   (`MainLayout.razor:98-103`).
3. `MudThemeProvider IsDarkMode="_settings.DarkMode"` emits CSS variables for the active palette (`:6`). `app.css`
   uses those variables, so the app bar, main background and table headers follow the theme (`app.css:70-84`).
4. On a settings save, `HandleSettingsChanged` → `InvokeAsync` → refresh `_settings` → `StateHasChanged`
   (`:107-111`).
5. When the circuit ends, `Dispose` unsubscribes (`:113`).

#### Dependencies
- **Uses:** `IAppSettingsService`, `ISnackbar` (only in the unused `Logout`), MudBlazor providers (`:7-9`).
- **Used by:** every page, as the default layout.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Component inheritance | `MainLayout.razor:1` | `LayoutComponentBase` supplies `Body` | Required for layouts |
| `IDisposable` on a component | `:2, 113` | Unsubscribes from the singleton's event | Without it, the singleton would keep every closed tab's layout alive |
| Event subscription with a method group | `:102` | `Settings.OnChange += HandleSettingsChanged` | Observer pattern |
| `InvokeAsync` + discard | `:107-111` | Moves the re-render onto this circuit's dispatcher; `_ =` ignores the task | The event fires on the saving circuit's thread |
| Object initialisers (nested) | `:57-96` | Builds the `MudTheme` | Declarative configuration |
| `readonly` field | `:57` | The theme instance never changes | Clarity |
| Static `using` | `_Imports.razor:6` | `InteractiveServer` without a prefix | Template convention |

#### Errors and edge cases
- **No error UI element:** `app.css:86-90` styles `#blazor-error-ui`, but no component renders that `<div>`, and there
  is no `ErrorBoundary`. ⚠️ Inferred: an unhandled exception in a component therefore ends the circuit without the
  standard "An unhandled error has occurred. Reload" banner, and the page just stops responding.
- `FocusOnNavigate Selector="h1"` (`Routes.razor:4`): pages use `MudText Typo.h4` for titles, which renders an `<h4>`
  ⚠️ Inferred, so no `h1` exists and focus is not moved on navigation (an accessibility gap).
- **Hard-coded values:** the user block "Admin User / admin@bangison.com" (`MainLayout.razor:24-27`); drawer colours
  (`app.css:6-9`) and the active-link blue (`app.css:38-42`) don't follow the palette. The drawer is dark in both
  themes by design.
- `Logout()` (`MainLayout.razor:117-118`) is never called (dead code).
- The welcome-message line is omitted when the message is empty (`:38-41`).

### 6.2 Employees feature

**Files:** `Components/Pages/Employees.razor`, `AddEmployee.razor`, `EditEmployee.razor`, `EmployeeDetails.razor`,
`Components/Shared/EmployeeForm.razor`

#### Responsibility
- **Owns:** list (search, department filter, status filter, sort, paging, bulk select and delete, single delete),
  create, edit, details. It also owns the UI-side duplicate-email check and all user feedback.
- **Does not own:** query logic or persistence (§5.1). `EmployeeForm` doesn't save; it only raises `OnSubmit`.

#### Public surface
| Item | Route / parameters | Where |
|---|---|---|
| `Employees` | `/employees` | `Employees.razor:1` |
| `AddEmployee` | `/employees/add` | `AddEmployee.razor:1` |
| `EditEmployee` | `/employees/edit/{Id:int}` | `EditEmployee.razor:1`, `[Parameter] int Id` (`:32`) |
| `EmployeeDetails` | `/employees/{Id:int}` | `EmployeeDetails.razor:1`, `[Parameter] int Id` (`:81`) |
| `EmployeeForm` | `Model` (required), `Departments` (required), `SubmitText`, `OnSubmit: EventCallback<Employee>`, `OnCancel: EventCallback` | `EmployeeForm.razor:65-70` |

#### Flow — list page (`Employees.razor`)
1. `OnInitializedAsync`: page size from settings, departments for the filter, then `LoadAsync` (`:176-181`).
2. `LoadAsync`:
   1. Clear the selection.
   2. Build an `EmployeeFilter` with named arguments and call `GetEmployeesAsync`.
   3. Adopt the clamped `Page`.
   4. Load the stats.
   (`:185-201`)
3. Toolbar events (search with a 300 ms debounce, department, status, sort) each set the value, reset `_page = 1` and
   reload (`:25-59`, `:203-232`). Pager events go through `TablePager` (`:140-142`, `:234-245`).
4. Single delete: menu → `AskToDelete` → `ConfirmDialog` → `DeleteConfirmedAsync` → snackbar → reload (`:125-129`,
   `:145-149`, `:280-296`).
5. Bulk delete: checkboxes bind `_selected` (`:81-82`) → "Delete selected (n)" (`:63-70`) → confirm (`:151-156`) →
   `DeleteManyAsync` → snackbar (full or partial) → reload (`:259-278`).

#### Flow — create and edit
- **Add:** load departments (`AddEmployee.razor:23-24`) → `EmployeeForm` validates → `SaveAsync` checks the email →
  `AddAsync` → snackbar → navigate to `/employees` (`:26-37`).
- **Edit:** `OnParametersSetAsync` loads departments and the employee (`EditEmployee.razor:38-44`), shows a loading or
  not-found state (`:11-19`) → `SaveAsync` checks the email excluding itself → `UpdateAsync` → snackbar → navigate
  (`:46-58`).
- **Details:** loads by ID (`EmployeeDetails.razor:87-92`); the menu offers Back, Edit and Delete with confirmation
  (`:22-43`, `:73-77`, `:94-102`).

#### Dependencies
- **Uses:** `IEmployeeService`, `IDepartmentService`, `IAppSettingsService` (list page), `NavigationManager`,
  `ISnackbar`; shared `TablePager`, `ConfirmDialog`, `StatCard`, `StatusChip`, `EmployeeAvatar`, `DetailItem`,
  `Formatters`.
- **Used by:** the router; `NavMenu`; links from `Home.razor:46`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Route templates with constraints | `EditEmployee.razor:1`, `EmployeeDetails.razor:1` | `{Id:int}` binds to `[Parameter] int Id` | Non-integer URLs don't match the route |
| `OnParametersSetAsync` vs `OnInitializedAsync` | `EditEmployee.razor:38`, `Employees.razor:176` | Reload when the route parameter changes vs once | Navigating from one employee to another reuses the component ⚠️ Inferred motivation |
| One-way `Value` + `ValueChanged` | `Employees.razor:25, 31, 41, 52` | Each change runs a handler that resets the page and reloads | Side effects on change, which `@bind` alone wouldn't give |
| Two-way binding `@bind-Value` / `@bind-Date` | `EmployeeForm.razor:6-49` | Fields write into the model | Standard form binding |
| `@bind-SelectedItems` with `HashSet<T>` | `Employees.razor:82, 173` | Row selection set | O(1) membership; MudTable's selection type |
| Generic components with `T=` | `Employees.razor:31, 41, 79, 140` | `MudSelect<int?>`, `MudSelect<EmployeeStatus?>`, `MudTable<Employee>`, `TablePager<Employee>` | Strongly typed values |
| Nullable enum with an explicit null item | `Employees.razor:45` | `(EmployeeStatus?)null` is the "All" option | One select covers "no filter" and each status |
| `ToStringFunc` delegate | `Employees.razor:32, 42`, `EmployeeForm.razor:26` | Shows names for ID values | Select values are IDs or enums, not display strings |
| `Enum.GetValues<T>()` (generic) | `Employees.razor:46`, `EmployeeForm.razor:42` | Lists every status | No hard-coded list |
| Named arguments | `Employees.razor:191-197`, `EditEmployee.razor:48` | Builds the filter and calls the email check by name | Readable and order-safe |
| Lambdas capturing loop/row context | `Employees.razor:127`, `:67` | `@(() => AskToDelete(context))` | Per-row handlers |
| `EventCallback<T>` | `EmployeeForm.razor:69, 81` | The form tells its parent "submitted" | Child-to-parent communication plus a parent re-render |
| `try/finally` | `EmployeeForm.razor:79-86` | `_saving` always resets | The button is never left disabled |
| Computed property with a block body | `Employees.razor:247-257` | `BulkDeleteMessage` builds "A, B and N more" | Keeps markup simple |
| Null-conditional in markup | `Employees.razor:102`, `EmployeeDetails.razor:60, 68` | `context.Department?.Name`, `DateHired?.ToString(...)` | Optional values render as empty |

#### Errors and edge cases
- **Validation:** client-side rule failures block submit (`EmployeeForm.razor:2, 76`).
- **Duplicate email:** pre-checked with a snackbar (`AddEmployee.razor:28-32`, `EditEmployee.razor:48-52`). The race
  is uncaught (§5.1).
- **Not found:** the Edit and Details pages show a warning and a back button (`EditEmployee.razor:15-19`,
  `EmployeeDetails.razor:12-16`). `UpdateAsync` returning `false` produces an error snackbar, but the page still
  navigates away (`EditEmployee.razor:55-57`).
- **Details delete ignores the result:** `EmployeeDetails.razor:99-100` always says "was deleted", even if someone else
  already removed the employee. (The list page handles this case at `Employees.razor:292-293`.)
- **Selection is cleared on every reload** (`Employees.razor:190`), so bulk delete can only affect visible rows.
- **Loading state:** the pager shows "Loading…" while `_loading` is true and there are no results yet
  (`TablePager.razor:12`).
- **Exceptions from services** (database down, FK or unique races) are **not caught** by these pages (§7).

### 6.3 Departments feature

**File:** `Components/Pages/Departments.razor`

#### Responsibility
- **Owns:** the add/rename form (one form, two modes), the paged department table with aggregates, the
  disabled-delete UX with explanatory tooltips, and delete confirmation and outcome messages.
- **Does not own:** the delete rule itself (enforced by the service and the database), or the aggregate calculation.

#### Public surface
- **Route:** `/departments` (`:1`).
- **Injects:** `IDepartmentService`, `IAppSettingsService`, `ISnackbar` (`:2-4`).

#### Flow
1. `OnInitializedAsync` → `LoadAsync` → `GetSummaryPageAsync(_page, _pageSize)`; adopt the clamped page (`:111-119`).
2. **Edit:** `StartEdit` copies the row into a new `Department`, which makes `IsEditing` true because `Id != 0`
   (`:141-145`, `:109`). Cancel calls `ResetForm` (`:147-151`).
3. **Save:** `NameExistsAsync` (excluding self when editing) → `UpdateAsync` or `AddAsync` → snackbar → reset → reload,
   inside `try/finally` for `_saving` (`:153-183`).
4. **Delete:** the button is disabled when `EmployeeCount > 0`, with the reason in the tooltip (`:74-78`, `:134-139`) →
   confirm (`:92-96`) → `DeleteAsync` → `switch` on the result → snackbar → reload (`:185-208`).

#### Dependencies
- **Uses:** `IDepartmentService`, `IAppSettingsService` (density, `:47`), `TablePager`, `ConfirmDialog`,
  `Formatters`.
- **Used by:** the router and `NavMenu.razor:18`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| `switch` expression with constant and `var` patterns | `:134-139` | Tooltip text for 0, 1 and n employees | Correct singular/plural wording |
| `switch` statement over an enum result | `:193-205` | Maps the service outcome to a message | One handler for each of the three outcomes |
| Computed property as a mode flag | `:109` | `IsEditing => _form.Id != 0` | Mode follows from the data; no separate flag to keep in sync |
| Ternaries in markup | `:13, 35, 37` | Title, icon and button text per mode | One form for both modes |
| `EditForm` with an entity as the model | `:16-17` | Validates `Department` attributes | Reuses the model rules |
| Copy, then null, before an async call | `:189-190` | `target = _departmentToDelete; _departmentToDelete = null;` | Closes the dialog immediately and avoids reading a field that may change while awaiting |
| Named argument with a conditional | `:158` | `excludeDepartmentId: IsEditing ? _form.Id : null` | Excludes self only when editing |

#### Errors and edge cases
- **Deleting a non-empty department** is blocked three times: in the UI (disabled button), in the service (pre-check),
  and in the database (Restrict FK). The race is caught (§5.2).
- **The department being edited is deleted:** the form resets (`:197`). Saving an edit after someone else deleted it
  produces "Department no longer exists." (`:167-168`).
- **Paging after a delete:** the service clamps the page and the page adopts it (`:117`).
- **New departments** appear on whatever page their name sorts to. The user stays on the current page and isn't
  taken to the new row.
- **Unique-name race:** not caught (§5.2).

### 6.4 Dashboard and Reports

**Files:** `Components/Pages/Home.razor`, `Components/Pages/Reports.razor`,
`Components/Shared/DepartmentHeadcountChart.razor`

#### Responsibility
- **Owns:** the read-only overviews: stat cards, the employees-per-department chart, recently added employees, and
  the report table with totals.
- **Does not own:** any writes. Aggregates come from the services.

#### Public surface
| Item | Where | Contract |
|---|---|---|
| `Home` | `/` (`Home.razor:1`) | Stats, chart, 5 most recent employees |
| `Reports` | `/reports` (`Reports.razor:1`) | Chart and a table ordered by headcount, with totals |
| `DepartmentHeadcountChart` | `Summaries` (required), `Height` (default `"320px"`) (`DepartmentHeadcountChart.razor:20-21`) | Single-series bar chart; sorts by count descending, then name |

#### Flow
- **Home:** `GetStatsAsync`, `GetRecentAsync(5)` and `GetSummariesAsync`, run **one after another**
  (`Home.razor:60-65`).
- **Reports:** `GetSummariesAsync` → order for the table → render the chart and table; the footer sums the column
  in memory (`Reports.razor:55-61`, `:40-45`).
- **Chart:** `OnParametersSet` sorts and builds `_labels`/`_series` (`DepartmentHeadcountChart.razor:63-76`). Two
  `MudHidden` blocks render the same chart template with horizontal labels on md+ screens and 45° labels on sm and
  below (`:11-16`, `:41-52`).

#### Dependencies
- **Uses:** `IEmployeeService`, `IDepartmentService`, `IAppSettingsService` (Reports density), MudBlazor `MudChart`,
  `StatCard`, `StatusChip`, `EmployeeAvatar`, `Formatters`.
- **Used by:** the router and `NavMenu`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| `RenderFragment` returned from a method (Razor template) | `DepartmentHeadcountChart.razor:55-61` | `Chart(axisOptions)` produces chart markup | One chart definition, two layouts |
| `OnParametersSet` for derived state | `:63-76` | Rebuilds the series when `Summaries` changes | Parameters can change after the first render |
| LINQ to Objects | `:66-74`, `Reports.razor:55, 60` | Sorting, projection, `Sum` | In-memory shaping of small lists |
| Object and collection initialisers | `:30-52, 72-75` | `ChartOptions`, `AxisChartOptions`, `List<ChartSeries>` | Declarative configuration |
| `const` string | `:25` | Bar colour | Contrast-checked for both themes (comment `:23-24`) |
| Expression-bodied computed property | `Reports.razor:55` | `TotalEmployees` | Derived value |

#### Errors and edge cases
- **No departments:** the chart shows "No departments yet." (`:4-7`).
- **Empty departments** appear as zero-height bars, because the summaries include them.
- **Chart workaround:** uses `ChartType.StackedBar` with a single series to control bar width (comment `:54`).
- **Sequential loads on Home** (`:62-64`) could run in parallel, but every service call creates its own context, so
  concurrency would be safe ⚠️ Inferred; this is optional performance work.

### 6.5 Settings feature

**File:** `Components/Pages/Settings.razor`

#### Responsibility
- **Owns:** editing the six settings. Theme, density and rows-per-page **save immediately**; company name and welcome
  message are a **validated form with Save/Discard**.
- **Does not own:** persistence or broadcasting (§5.3), or applying the settings (the layout and pages read them).

#### Public surface
- **Route:** `/settings` (`:1`).
- **Injects:** `IAppSettingsService SettingsService` (renamed from `Settings` because the component class is itself
  called `Settings`), `ISnackbar` (`:2-3`).

#### Flow
1. `OnInitialized`: `_saved = Current` (read-only view); `_companyForm = Current.Clone()` (editable) (`:77-81`).
2. Instant controls → `OnDarkModeChanged` / `OnDensityChanged` / `OnPageSizeChanged` → `SaveAsync(lambda, message)`
   (`:83-90`).
3. `SaveAsync`: clone `Current`, apply the lambda, `SettingsService.SaveAsync`, refresh `_saved`, success snackbar;
   on any exception, an error snackbar; `_saving` is reset in `finally` (`:107-126`).
4. Company form: `EditForm` validates the `AppSettings` attributes (`:34-35`); Save and Discard are enabled only when
   something changed (`:43-46`, `:73-75`); `SaveCompanyAsync` saves both fields, then resets the form from `_saved`
   (`:92-104`).

#### Dependencies
- **Uses:** `IAppSettingsService`, `ISnackbar`, `AppSettings.PageSizeOptions` (`:59`).
- **Used by:** the router and `NavMenu.razor:30`.

#### C# concepts used here
| Concept | Where | What it does here | Why |
|---|---|---|---|
| Higher-order function (`Action<AppSettings>`) | `:107, 113` | Each control passes "how to change the settings" as a lambda | One save routine for five fields |
| Lambdas with statement bodies | `:96-100` | Sets two properties in one change | Company name and welcome message save together |
| Expression-bodied methods returning `Task` | `:83-90` | `=> SaveAsync(...)` without `async` | No extra state machine needed |
| `catch (Exception)` at the UI boundary | `:118-121` | Shows the error message | Keeps the circuit alive on DB errors or the service's `ArgumentException` |
| Clone for edit buffers | `:80, 104, 112` | The form edits a copy; each save starts from the latest saved state | Unsaved typing isn't saved by an unrelated toggle (comment `:106`) |
| `??` for binding nulls | `:74-75, 94-95` | MudBlazor may set a cleared text field to `null` ⚠️ Inferred | Avoids a `NullReferenceException` on `.Trim()` |

#### Errors and edge cases
- **Empty company name:** blocked by `[Required]` (`AppSettings.cs:28`) before submit; the service also throws as a
  second guard (`AppSettingsService.cs:37-40`).
- **Empty welcome message:** allowed; hides the line in the app bar.
- **Saving in another tab:** this page's `_saved` isn't refreshed live (it doesn't subscribe to `OnChange`). Its next
  save starts from the service's latest `Current` (`:112`), so other fields aren't overwritten with stale values. The
  displayed values can be stale until the page is reloaded.

### 6.6 Shared components and helpers

**Files:** `Components/Shared/*.razor`, `Helpers/Formatters.cs`

| Component | Responsibility | Public surface | Notable concepts and edge cases |
|---|---|---|---|
| `TablePager<T>` | Pager row for a server-paged table: summary text, rows per page, page buttons. **Holds no state.** | `Result` (required), `ItemName`, `ItemNamePlural`, `EmptyText`, `PageSizeOptions` (default `AppSettings.PageSizeOptions`), `Disabled`, `PageChanged`, `PageSizeChanged` (`TablePager.razor:37-45`) | Generic component `@typeparam T` (`:1`); `default!` null-forgiving (`:37`); shows "Loading…" instead of the empty text while disabled (`:12`) |
| `ConfirmDialog` | Modal confirmation overlay | `Visible`, `Title`, `Message`, `ConfirmText` (default "Delete"), `OnConfirm`, `OnCancel` (`ConfirmDialog.razor:13-19`) | Controlled by a parameter, not `IDialogService`; `AutoClose="false"` means clicking the backdrop doesn't cancel (`:1`) |
| `EmployeeForm` | See §6.2 | | |
| `DepartmentHeadcountChart` | See §6.4 | | |
| `StatCard` | Icon, label and value card | `Title`, `Value` (required), `Icon`, `IconColor` (`StatCard.razor:12-15`) | Presentational only |
| `StatusChip` | Coloured status label | `Status` (required) (`StatusChip.razor:5`) | `switch` expression maps status to colour (`:7-12`) |
| `EmployeeAvatar` | Initials avatar with a stable colour | `Initials` (required), `ColorSeed`, `AvatarSize` (`EmployeeAvatar.razor:9-13`) | `static readonly` palette (`:4-7`); colour = `Palette[Math.Abs(seed) % 6]` (`:15`) |
| `DetailItem` | Label with a value **or** custom content | `Label` (required), `Value?`, `ChildContent?` (`DetailItem.razor:14-16`) | `RenderFragment? ChildContent` slot (`:16`); "—" for blank values (`:9`) |
| `Formatters` (static) | Display formatting | `Money(decimal)` → `"K{amount:N0}"` (`Formatters.cs:7`); `StatusText(EmployeeStatus)` (`:9-15`) | Format specifier `N0`; `switch` expression with a `_` fallback |

⚠️ Inferred: `Formatters.Money` uses the server's current culture for digit grouping, because no culture is
configured anywhere. The output would change if the server's regional settings change.

---

## 7. Cross-cutting concerns

### Logging
- **Where:** framework logging only, configured in `appsettings.json` (`Logging:LogLevel`: Default `Information`,
  `Microsoft.AspNetCore` `Warning`, EF Core SQL commands `Warning`).
- **How applied:** the default providers set up by `WebApplication.CreateBuilder` (`Program.cs:7`). ⚠️ Inferred:
  console and debug output.
- **Application code doesn't log:** there are no `ILogger` injections or calls anywhere (searched every `.cs` and
  `.razor` file). Handled failures (snackbars, the `DbUpdateException` catch in `DepartmentService.cs:134`) leave no
  trace in the logs.

### Configuration
- **Where:** `appsettings.json` (connection string, log levels, `AllowedHosts`); `Properties/launchSettings.json`
  (URLs, environment, development only).
- **How applied:** `builder.Configuration.GetConnectionString("DefaultConnection")` (`Program.cs:18`) is the only
  configuration read in app code. There are no options classes (`IOptions<T>`).
- **Runtime settings** (theme, company name and so on) are **data, not configuration**: they live in the `AppSettings`
  table and go through `IAppSettingsService` (§5.3).
- ⚠️ Inferred: no `appsettings.Development.json` exists, so the same connection string applies in every environment
  unless environment variables override it.

### Validation
| Level | Where | Applied by |
|---|---|---|
| Field rules (UI) | Attributes on `Employee`, `Department`, `AppSettings` (§3) | `DataAnnotationsValidator` inside `EditForm`: `EmployeeForm.razor:2`, `Departments.razor:17`, `Settings.razor:35`. Submit runs only on `OnValidSubmit` |
| Uniqueness (UI pre-check) | `EmailExistsAsync`, `NameExistsAsync` | Called by pages before saving (`AddEmployee.razor:28`, `EditEmployee.razor:48`, `Departments.razor:158`) |
| Business rules (service) | Delete only empty departments (`DepartmentService.cs:124-127`); settings: non-empty name, allowed page size (`AppSettingsService.cs:37-45`); paging bounds (`EmployeeService.cs:23, 54`) | Inside the service methods |
| Normalisation (service) | Trimming (`EmployeeService.cs:164-170`, `DepartmentService.cs:88-89, 143-144`, `AppSettingsService.cs:34-35`) | Before every write |
| Integrity (database) | NOT NULL, lengths, unique indexes, FK with Restrict (`AppDbContext.cs`) | SQL Server; the final guarantee |

Services **don't** re-run the data-annotation rules themselves. A caller that bypasses the UI could save values the
attributes forbid, for example a salary of 0, which only the database would catch, if at all.

### Authentication and authorisation
- **None.** There is no `AddAuthentication`, no `[Authorize]` and no `AuthenticationState` anywhere (searched).
  Every visitor can do everything.
- Traces: a hard-coded "Admin User" in the drawer (`MainLayout.razor:24-27`) and an unused `Logout()` that says
  "Authentication is not implemented yet." (`:117-118`).
- `UseAntiforgery()` (`Program.cs:45`) protects form posts against cross-site request forgery. It is not
  authentication.

### Error handling
| Kind of failure | Handling | Where |
|---|---|---|
| Expected business outcomes | Return values (`bool`, `int`, `DepartmentDeleteResult`, `null`), turned into snackbars by pages | §5, §6 |
| Delete race (FK) | `catch (DbUpdateException)` → `HasEmployees` | `DepartmentService.cs:134-138` |
| Settings save failures | `catch (Exception)` → error snackbar | `Settings.razor:118-121` |
| Busy flags | `try/finally` | `EmployeeForm.razor:79-86`, `Departments.razor:156-182`, `Settings.razor:110-125` |
| Startup failures | Unhandled → process exits | `Program.cs:28-37` |
| **Everything else** (database down during page loads, unique-index races on add/update, FK violation when adding to a just-deleted department) | **Unhandled.** The exception escapes the component | Pages and services |

⚠️ Inferred consequence of the last row: with no `ErrorBoundary` and no `#blazor-error-ui` element (§6.1), an
unhandled exception kills the user's circuit, and the page stops responding without an on-screen explanation. In
Development, the detailed error goes to the server log (framework logging).

### Concurrency and threading
- Each service call uses its own `DbContext` (factory pattern), so pages and the singleton can run queries
  concurrently without sharing a context.
- Cross-circuit UI updates go through `InvokeAsync` (`MainLayout.razor:107`).
- No optimistic concurrency tokens exist; the last write wins for employees, departments and settings.

### Styling and theming
- MudBlazor theme in `MainLayout.razor:57-96`; custom CSS in `wwwroot/app.css` uses `var(--mud-palette-*)` for
  surfaces that must follow dark mode (`app.css:67-84`). The drawer colours are fixed on purpose (`app.css:6-9`).

---

## 8. Extension points

This project has **no controllers or HTTP API**: every feature is a Razor page talking to a service. The recipes
below follow the existing patterns.

### Add a new page (screen)
1. Create `Components/Pages/<Name>.razor` with `@page "/<route>"`; use `{Id:int}` for parameters, as in
   `EditEmployee.razor:1`.
2. `@inject` the service interfaces you need. `_Imports.razor` already brings in `Models`, `Services`, `Shared` and
   `MudBlazor`.
3. Load data in `OnInitializedAsync` (or `OnParametersSetAsync` if the route has parameters).
4. Add a `MudNavLink` in `Components/Layout/NavMenu.razor`.
5. Nothing to register: the router discovers `@page` components automatically (`Routes.razor:1`).

### Add a new business operation to an existing feature
1. Add the method signature to the interface, e.g. `Services/IEmployeeService.cs`.
2. Implement it in the service. Create a context with `await using var db = await _factory.CreateDbContextAsync();`,
   use `AsNoTracking()` for reads, and return a result value for expected outcomes (like `DepartmentDeleteResult`)
   rather than throwing.
3. Call it from the page and turn the result into a snackbar.

### Add a new entity / table (e.g. "Projects")
1. **Model:** `Models/Project.cs` with validation attributes (as in `Models/Department.cs`).
2. **Mapping:** add `public DbSet<Project> Projects => Set<Project>();` and a `modelBuilder.Entity<Project>(...)`
   block in `Data/AppDbContext.cs` (keys, lengths, indexes, FK delete behaviour).
3. **Migration:** stop the running app (it locks the build output), run `dotnet ef migrations add AddProjects`, and
   review the generated `Up()`. Startup applies it automatically (`Program.cs:32`).
4. **Service:** `Services/IProjectService.cs` + `Services/ProjectService.cs` taking
   `IDbContextFactory<AppDbContext>`.
5. **Register** it in `Program.cs` next to `:21-22`: `builder.Services.AddScoped<IProjectService, ProjectService>();`
   (scoped unless it must be shared across tabs).
6. **Seed** (optional): extend `Data/DbSeeder.cs`, or use `HasData` for fixed reference data (as for `AppSettings`).
7. **UI:** pages as above; reuse `TablePager<T>` with `PagedResult<T>` for lists.

### Add a filter to the employee list
1. Add an optional parameter **at the end** of `EmployeeFilter` (`Models/EmployeeQueryModels.cs:12-18`), so existing
   positional calls keep compiling.
2. Add a `Where` clause in `EmployeeService.GetEmployeesAsync` **before** the `CountAsync` (`EmployeeService.cs:49`),
   so the total reflects it.
3. Add the control, a field and an `On…Changed` handler that sets `_page = 1` and calls `LoadAsync` in
   `Employees.razor`; pass it by name in `LoadAsync` (`:191-197`).

### Add a setting
1. Add the property (with a default constant and attributes) to `Models/AppSettings.cs`.
2. Map it in `AppDbContext.cs:18-36`, and add it to the `HasData` seed (`:27-35`) so a migration sets the existing row.
3. `dotnet ef migrations add Add<Setting>`. If needed, check the generated `UpdateData` / `defaultValue`, as for
   `AddWelcomeMessage`.
4. Normalise or validate it in `AppSettingsService.SaveAsync` (`:30-45`).
5. Add a control in `Settings.razor` that calls `SaveAsync(s => s.NewSetting = value, "…")` (`:83-90` shows the
   pattern).
6. Read it where it applies through `IAppSettingsService.Current`. If it must update live in other tabs, subscribe to
   `OnChange` **and unsubscribe in `Dispose`** (`MainLayout.razor:102, 113`).

### Add a reusable component
1. Create `Components/Shared/<Name>.razor` (the namespace is already imported).
2. Inputs are `[Parameter]` (`[EditorRequired]` when mandatory); outputs are `EventCallback`/`EventCallback<T>`; use
   `@typeparam T` for generic components (`TablePager.razor:1`). Keep data access in the parent page.

### Add an HTTP API endpoint (not currently used)
⚠️ Inferred recipe: there is no existing example in this codebase.
1. In `Program.cs`, after `builder.Build()` and before `app.Run()`, map a minimal-API endpoint, e.g.
   `app.MapGet("/api/employees", async (IEmployeeService s, int page = 1) => await s.GetEmployeesAsync(new EmployeeFilter(Page: page)));`.
2. Reuse the existing services; they don't depend on Blazor.
3. Add authentication before exposing it: the app currently has none (§7).

### Add logging
1. Inject `ILogger<T>` into a service constructor (or `@inject ILogger<PageName> Logger` in a page).
2. Log inside existing `catch` blocks first (`DepartmentService.cs:134`, `Settings.razor:118`). Levels are controlled
   in `appsettings.json`.

### Add global error handling
⚠️ Inferred recipe (no existing implementation):
1. Wrap `@Body` in `MainLayout.razor:48` with `<ErrorBoundary>` and provide an `<ErrorContent>` message.
2. Add the `<div id="blazor-error-ui">` that `app.css:86` already styles.

---

## 9. Review notes (consolidated)

Each item below is cited in context above. They are listed here so a reviewer can find them in one place.

| # | Finding | Where | Severity (judgement) |
|---|---|---|---|
| 1 | No authentication or authorisation; every visitor has full access | §7 | High if the app is exposed beyond a trusted network |
| 2 | Unhandled exceptions end the circuit with no visible error UI (no `ErrorBoundary`, no `#blazor-error-ui` element) | `MainLayout.razor:48`, `app.css:86` (⚠️ Inferred behaviour) | Medium |
| 3 | Unique-index races (duplicate email or department name) throw an uncaught `DbUpdateException` | `EmployeeService.cs:124, 140`, `DepartmentService.cs:94, 110` | Low–Medium |
| 4 | Adding or updating an employee whose department was just deleted throws an uncaught FK violation | `EmployeeService.cs:124, 140` | Low |
| 5 | The Details page reports "deleted" even when nothing was deleted | `EmployeeDetails.razor:99-100` | Low |
| 6 | No application logging; handled failures leave no trace | §7 | Medium for operations |
| 7 | Services don't re-validate data-annotation rules; the UI is the only enforcement for some rules (e.g. salary > 0) | §7 Validation | Low (UI-only app today) |
| 8 | Last write wins; no concurrency tokens | §7 | Low |
| 9 | `PageSizeOptions` is a public mutable array | `AppSettings.cs:22` | Low |
| 10 | Cross-service constant coupling (`EmployeeService.MaxPageSize` used by `DepartmentService`) | `DepartmentService.cs:30` | Low |
| 11 | Dead code: `MainLayout.Logout`; unused template usings | `MainLayout.razor:117-118`, `_Imports.razor:1-2, 7` | Trivial |
| 12 | `FocusOnNavigate` targets `h1`, but pages render `h4` titles | `Routes.razor:4` (⚠️ Inferred) | Low (accessibility) |
| 13 | Floating package versions (`8.*`, `10.*`) make builds non-reproducible | `EmployeeManagementSystem.csproj:10-12` | Low |
| 14 | `bin/` and `obj/` are tracked in git | repository | Low (noise) |
