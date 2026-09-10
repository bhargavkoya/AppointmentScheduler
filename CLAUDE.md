# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Doctor/patient appointment scheduling web app. Single ASP.NET Core MVC project (`ApplicationScheduling`) targeting **.NET 5.0** (out of support). Solution: `ApplicationScheduling.sln`. This directory is the git root.

**Related docs:**
- `DECISIONS.md` — architecture decision log; read it before making structural changes.
- `docs/dotnet-10-migration-plan.md` — phased plan for the in-progress upgrade to .NET 10.

## Commands

Run all commands from the repo root (where `ApplicationScheduling.sln` lives).

```bash
dotnet build ApplicationScheduling.sln
dotnet run --project ApplicationScheduling          # serves https://localhost:5001 / http://localhost:5000
```

EF Core migrations (needs `dotnet tool install --global dotnet-ef`):

```bash
dotnet ef migrations add <Name> --project ApplicationScheduling
dotnet ef database update --project ApplicationScheduling
```

Note: pending migrations are also applied automatically at app startup (see `DbInitializer`), so `database update` is rarely needed manually.

There is **no test project** and no linter configured.

## Configuration & secrets

**No real secrets belong in `appsettings.json` or any committed file.** `appsettings.json` holds only empty placeholders / non-secret defaults. Supply real values locally with user-secrets and in deployment with environment variables:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=ApplicationScheduller;Username=appuser;Password=<pw>" --project ApplicationScheduling
dotnet user-secrets set "Mailjet:ApiKey" "<key>" --project ApplicationScheduling
dotnet user-secrets set "Mailjet:ApiSecret" "<secret>" --project ApplicationScheduling
dotnet user-secrets set "SeedAdmin:Password" "<strong password>" --project ApplicationScheduling
```

Config keys the app reads: `ConnectionStrings:DefaultConnection`; `Mailjet:ApiKey|ApiSecret|FromEmail|FromName`; `Notifications:AdminEmail` (recipient of the "appointment created" notice); `SeedAdmin:Email|Name|Password`. Env-var form uses `__` for `:` (e.g. `ConnectionStrings__DefaultConnection`). Historic note: Mailjet keys and a DB password were committed in early history and remain in git log — the Mailjet keys must be rotated by the repo owner (see `DECISIONS.md`).

## Database setup

- Uses **PostgreSQL via Npgsql** for EF Core. (`Microsoft.EntityFrameworkCore.SqlServer` is referenced in the csproj but unused — slated for removal in the .NET 10 migration.)
- `Startup.ConfigureServices` picks the connection string by environment:
  - `ASPNETCORE_ENVIRONMENT=Development` → `ConnectionStrings:DefaultConnection` (from user-secrets / `appsettings.json`). Running locally requires a reachable Postgres instance.
  - Any other environment → parses a Heroku-style `DATABASE_URL` env var (`postgres://user:pass@host:port/db`) into an Npgsql connection string.
- On first run `DbInitializer.Initalize()` (called from `Startup.Configure`) applies pending migrations, ensures the `Admin`/`Doctor`/`Patient` roles exist, and seeds an admin user from `SeedAdmin:*`. If `SeedAdmin:Password` is unset, admin seeding is skipped with a warning.

## Architecture

**Hosting**: classic `Program.cs` + `Startup.cs` (not minimal hosting). Razor **runtime compilation** is enabled, so `.cshtml` edits take effect without a rebuild (`ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` is set in `launchSettings.json`).

**Two request styles share the same project:**

1. Server-rendered MVC — `AccountController` (Identity login/register/logoff), `HomeController`, and `AppointmentController` (renders the calendar page; passes doctor/patient/duration lists via `ViewBag`). Views in `Views/`, layout in `Views/Shared/_Layout.cshtml`. Default route `{controller=Home}/{action=Index}/{id?}`.
2. JSON API — `Controllers/Api/AppointmentApiController.cs`, route prefix `api/Appointment`. This is the real appointment CRUD surface, consumed entirely by `wwwroot/js/script-custom-calendar.js` (FullCalendar month/week/day view) via jQuery AJAX. Endpoints: `SaveCalendarData` (POST, add or update), `GetCalendarData`, `GetCalendarDataById/{id}`, `DeleteAppoinment/{id}`, `ConfirmEvent/{id}`.

**Response envelope**: every API method returns `CommonResponse<T>` (`status` int, `message` string, `data`/`dataenum` payload). Conventions from `Utility/Helper.cs`: `success_code = 1`, `failure_code = 0`; `AppointmentService.AddUpdate` specifically returns `1` for an update and `2` for a create, and the JS branches on those values. Helper also holds the role-name constants (`Admin`/`Doctor`/`Patient`) and all user-facing message strings.

**Service layer**: `IAppointmentService` / `AppointmentService` (registered transient) holds all appointment business logic and DB access — controllers stay thin. It maps between the `Appointment` entity and `AppointmentVM`, and formats dates as strings (`yyyy-MM-dd HH:mm:ss`) for the client. `AddUpdate` also sends creation emails.

**Role-based scoping**: `AppointmentApiController`'s constructor pulls the current user's id and role from claims via `IHttpContextAccessor`. `GetCalendarData` then returns only that user's appointments — patients see `PatientsEventsById`, doctors see `DoctorsEventsById`, and any other role (admin) sees the appointments of the `doctorId` passed in the query string.

**Approval workflow**: appointments are created with `IsDoctorApproved = false` (red in the calendar); a doctor calls `ConfirmEvent` to approve (green). Any edit via `AddUpdate` resets `IsDoctorApproved` to `false`.

**Auth**: ASP.NET Core Identity with `ApplicationUser : IdentityUser` (adds a `Name` field). `ApplicationDbContext : IdentityDbContext<ApplicationUser>` adds one `DbSet<Appointment>`. `AppointmentController` requires `[Authorize]`; access-denied redirects to `/Home/AccessDenied`. The logged-in user's display name is also stashed in session as `ssuserName`.

**Email**: `Utility/EmailSender.cs` implements Identity's `IEmailSender` using the **Mailjet** API, reading `Mailjet:*` from configuration. When key/secret/from-address are not configured it logs a warning and no-ops (so appointment creation still works locally without Mailjet). The admin-notification recipient comes from `Notifications:AdminEmail` in `AppointmentService.AddUpdate`.

**Front-end**: jQuery + Bootstrap + FullCalendar + select2 + a `notify` plugin. Vendored under `wwwroot/lib/` (`libman.json` manages only select2). App JS is `wwwroot/js/script-custom-calendar.js`; the add/edit modal markup is `Views/Shared/_AddEditAppointment.cshtml`.

## Gotchas

- `DbInitializer.Initalize` and the `DeleteAppoinment` route are misspelled in the source — match the existing spelling when referencing them.
- The API uses blocking `.Result` on async service calls in several places.
- `.NET 5.0` is EOL; the SDK will emit `NETSDK1138`. Build still succeeds on newer SDKs. See the migration plan.

## Branch & PR workflow

- Branch off `master`; never commit to `master` directly. Naming: `feat/*`, `fix/*`, `chore/*`, `docs/*`, and `migration/net10-*` for upgrade steps.
- Keep secrets out of every commit. If a value is sensitive it goes to user-secrets / env vars, never a tracked file.
- Open a PR per logical step with a description of what changed and how it was verified (`dotnet build`, manual run, etc.).
