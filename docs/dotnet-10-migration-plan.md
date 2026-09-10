# .NET 5 → .NET 10 Migration Plan

Status: **Phase A complete** · Owner: repo maintainer · Related: `DECISIONS.md` ADR-0002/0004/0005/0006/0008

This plan upgrades `ApplicationScheduling` from `net5.0` (end-of-life) to `net10.0` (LTS) at the
**Moderate** scope agreed in ADR-0002. Each phase is a branch off `master` and a separate PR so problems
stay isolated and reviewable.

---

## 1. Current state

| Aspect | Today |
| --- | --- |
| TFM | `net5.0` (EOL — SDK emits `NETSDK1138`) |
| Hosting | `Program.cs` + `Startup.cs` (generic host) |
| Web stack | MVC + Razor views, Razor **runtime compilation** enabled |
| Data | EF Core 5 + **Npgsql** (PostgreSQL); unused `EntityFrameworkCore.SqlServer` package also referenced |
| Migrations | one: `20220614141724_PostgresAdded`; auto-applied at startup by `DbInitializer` |
| Auth | ASP.NET Core Identity (`ApplicationUser : IdentityUser`), roles Admin/Doctor/Patient |
| Email | Mailjet via `Mailjet.Api` 2.0.2 + `Newtonsoft.Json` (transitive, used directly in `EmailSender`) |
| Config | secrets externalised to config keys as of ADR-0004 (user-secrets / env vars) |
| Tests | none |
| CI | none |

### Package inventory (`ApplicationScheduling.csproj`)

| Package | Current | Action |
| --- | --- | --- |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 5.0.15 | → `10.0.*` |
| `Microsoft.AspNetCore.Identity.UI` | 5.0.15 | → `10.0.*` |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | 5.0.17 | → `10.0.*` |
| `Microsoft.EntityFrameworkCore.SqlServer` | 5.0.15 | **remove** (ADR-0006) |
| `Microsoft.EntityFrameworkCore.Tools` | 5.0.15 | → `10.0.*` |
| `Microsoft.VisualStudio.Web.CodeGeneration.Design` | 5.0.2 | → `10.0.*`, or **remove** if no scaffolding is used |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 5.0.10 | → `10.0.*` |
| `Mailjet.Api` | 2.0.2 | evaluate latest `Mailjet.Api`; keep pinned if the newer client changes the API surface |
| `Newtonsoft.Json` (transitive) | — | keep only if still used by `EmailSender`; drop if the Mailjet client no longer needs `JObject`/`JArray` |

> Verify exact 10.x patch versions against nuget.org at implementation time; do not hand-guess patch numbers.

---

## 2. Target state

- `net10.0`, `LangVersion` default (C# 14).
- Single `Program.cs` using `WebApplication.CreateBuilder(args)` (ADR-0005); identical middleware order and
  DI registrations; `DbInitializer.Initalize()` still invoked once at startup.
- Only the Npgsql EF provider referenced.
- Build clean: **0 warnings, 0 errors** (the EOL warning is gone; treat any new analyzer warning as a to-fix).
- App runs locally against PostgreSQL with the same behaviour: login/register, calendar CRUD API,
  doctor-approval flow, Mailjet email (or the configured no-op when unconfigured).

---

## 3. Guiding principles

1. **One concern per PR.** Retarget, hosting rewrite, and dependency pruning are separate commits/PRs.
2. **Behaviour-preserving.** No feature or endpoint changes in this migration. Anything tempting
   (System.Text.Json, nullable refs, async cleanup, tests) is a *follow-up* ADR + PR.
3. **Green build gate.** Every PR must `dotnet build` with 0 warnings/0 errors and pass a manual smoke test.
4. **No secrets in any commit.** Real values stay in user-secrets / env (ADR-0004).
5. **Reversible.** Each PR reverts cleanly; `master` stays deployable throughout.

---

## 4. Pre-flight checklist (do before Phase A)

- [ ] .NET 10 SDK installed (`dotnet --list-sdks` shows `10.*`). ✔ confirmed on the dev machine.
- [ ] Prep PR merged: `.gitignore`, secret externalisation, `DECISIONS.md`, this plan.
- [ ] Mailjet keys **rotated** by the owner (ADR-0004 follow-up) — independent of code work.
- [ ] A local PostgreSQL instance reachable, and `dotnet user-secrets` populated
      (`ConnectionStrings:DefaultConnection`, `SeedAdmin:Password`, optionally `Mailjet:*`).
- [ ] Capture a baseline: `git checkout master`, run the app, note working flows (login, create appointment,
      confirm, delete, doctor filter) and the current DB schema (`\d "Appointments"` etc.).
- [ ] Add `global.json` pinning the 10.x SDK feature band so the build is reproducible.

---

## 5. Phases

### Phase A — `migration/net10-retarget` — ✅ complete (build + runtime smoke test passed)

**Goal:** compile and run on `net10.0` with minimal edits; keep `Startup.cs`.

1. ✅ `ApplicationScheduling.csproj`: `<TargetFramework>net10.0</TargetFramework>`; `global.json` added
   (pins SDK `10.0.401`, `rollForward: latestFeature`).
2. ✅ Packages bumped: Identity.EntityFrameworkCore / Identity.UI / Mvc.Razor.RuntimeCompilation /
   EntityFrameworkCore.SqlServer / EntityFrameworkCore.Tools → `10.0.12`; CodeGeneration.Design → `10.0.2`;
   Npgsql.EntityFrameworkCore.PostgreSQL → `10.0.3`. `Mailjet.Api` left at `2.0.2` (no 10.x; evaluated in
   Phase C). SqlServer package intentionally left in for this PR.
3. ✅ `dotnet build` → **0 errors**. Warnings: only `NU1901` (low-severity, transitive from
   CodeGeneration.Design → `NuGet.Packaging` / `NuGet.Protocol` 6.12.1) — clears in Phase C when that package
   is removed. **No** obsolete-API or source-compat warnings; **no** Newtonsoft version conflict.
4. ✅ **Break #1 found & fixed:** `Properties/launchSettings.json` had `"dotnetRunMessages": "true"`
   (string). .NET 10's launch-settings parser is strict and rejected it (`JSON value could not be converted
   to System.Boolean`), which made the profile silently not apply → app fell through to the Heroku
   `DATABASE_URL` branch and `NullReferenceException`. Fixed to boolean `true`.
5. ✅ **Break #2 found & fixed (see ADR-0008):** EF Core 9+ makes `PendingModelChangesWarning` a hard
   error, and Npgsql 6+ changed the default `DateTime` mapping to `timestamptz`. `Migrate()` threw and
   startup then crashed in `DbInitializer` (`relation "AspNetRoles" does not exist`). Fixed by pinning
   `Appointment.StartDate`/`EndDate` to `timestamp without time zone` in `ApplicationDbContext.OnModelCreating`
   and regenerating one clean EF 10 migration (`20260910211338_InitialCreate`); the EF 5
   `20220614141724_PostgresAdded` + snapshot were removed.
6. ✅ Runtime smoke test — against PostgreSQL 16 (`postgres:16-alpine`), `ASPNETCORE_ENVIRONMENT=Development`,
   connection string + `SeedAdmin:Password` via env/user-secrets:
   - Startup applies `InitialCreate` (history row `ProductVersion 10.0.12`); all 9 tables created.
   - `DbInitializer` seeds roles `Admin`/`Doctor`/`Patient` and the admin user from `SeedAdmin:*` config.
   - Register Doctor + Patient (auto sign-in) → 200; seeded-admin form login → 200.
   - `POST /api/Appointment/SaveCalendarData` → `status 2` "added"; `GET GetCalendarData` /
     `GetCalendarDataById` return the row with doctor/patient names resolved.
   - **Timestamp round-trip is byte-identical:** in `2026-09-15 14:30:00`, dur 90 → stored & returned
     `StartDate 2026-09-15 14:30:00` / `EndDate 2026-09-15 16:00:00`; DB column type
     `timestamp without time zone`. The §6 risk is resolved by ADR-0008.
   - `ConfirmEvent` flips `IsDoctorApproved` → true; a subsequent update resets it → false and applies new
     times; `DeleteAppoinment` removes the row.
   - Mailjet unconfigured → appointment creation still succeeds, no exception (the no-op guard works).

**Acceptance:** builds 0/0 ✅; baseline flows pass ✅; appointment start/end times identical to baseline ✅.

### Phase B — `migration/net10-minimal-hosting` — ✅ complete

**Goal:** merge `Startup.cs` into `Program.cs` (ADR-0005). Pure refactor.

**Done:** `Program.cs` rewritten with `WebApplication.CreateBuilder(args)`; `ConfigureServices` body →
`builder.Services.*` verbatim (same DbContext env-branch logic, same registrations); `Configure` body →
top-level middleware in the **same order**; `IDbInitializer.Initalize()` now runs from an
`app.Services.CreateScope()` before `app.Run()`. `Startup.cs` deleted. Build 0 errors (only `NU1901`).
Smoke test (PostgreSQL 16, Development): app boots on minimal hosting, `GET /` 200, register 200, calendar
API returns the `CommonResponse` envelope, `DbInitializer` runs via the scope. Two compile fixes needed:
add `using Microsoft.AspNetCore.Builder;` (+ `Hosting`/`DependencyInjection`/`Configuration` — no
ImplicitUsings in this project) and fully-qualify `ApplicationScheduling.DbInitializer.DbInitializer` to
disambiguate from the namespace.

---

<details><summary>Original Phase B plan</summary>

1. Replace `Program.cs` with `WebApplication.CreateBuilder(args)`; move `ConfigureServices` body to
   `builder.Services.*`; move `Configure` body to run against `app`, **preserving order**:
   `UseDeveloperExceptionPage`/`UseExceptionHandler`+`UseHsts` → `UseHttpsRedirection` → `UseStaticFiles`
   → `UseRouting` → `UseAuthentication` → `UseAuthorization` → `UseSession` → (`DbInitializer.Initalize()`)
   → endpoint mapping.
2. Resolve `IDbInitializer` from `app.Services.CreateScope()` and call `Initalize()` before `app.Run()`
   (it currently runs via DI parameter injection into `Configure`).
3. Delete `Startup.cs`.
4. Keep `ASPNETCORE_HOSTINGSTARTUPASSEMBLIES` in `launchSettings.json` (still needed for runtime compilation).
5. `dotnet build` 0/0; `dotnet run`; full smoke test again.
6. PR — diff should be almost entirely `Program.cs`.

**Acceptance:** identical runtime behaviour; no `Startup` type remains.

</details>

### Phase C — `migration/net10-dependency-cleanup`

**Goal:** trim dependencies (ADR-0006).

1. Remove `Microsoft.EntityFrameworkCore.SqlServer`. `dotnet build` — confirm nothing referenced it.
2. Decide on `Microsoft.VisualStudio.Web.CodeGeneration.Design`: keep at `10.0.*` only if scaffolding is
   actually used; otherwise remove.
3. If the updated Mailjet client no longer needs `Newtonsoft.Json`, rewrite the `JObject`/`JArray` usage in
   `EmailSender` with the client's native types (or `System.Text.Json.Nodes`) and drop the package.
4. `dotnet build` 0/0; smoke-test **email** specifically (configure Mailjet in user-secrets and send one),
   plus a quick pass of the other flows.
5. PR.

**Acceptance:** builds 0/0; email sends; dependency list is only what the code uses.

### Phase D — `migration/net10-hardening` (optional, same milestone)

Low-risk polish once C is green:

1. Turn on `<Nullable>enable</Nullable>` **or** at least `<AnalysisLevel>latest</AnalysisLevel>` +
   `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and clear the fallout (may be large — can be its
   own ADR if so).
2. Replace blocking `.Result` / `.GetAwaiter().GetResult()` in `AppointmentApiController` and
   `AppointmentService` with `async`/`await` end to end.
3. Add a minimal CI workflow (`.github/workflows/build.yml`: restore + build + `dotnet format --verify-no-changes`).

**Acceptance:** CI green on the PR; no behaviour change.

---

## 6. Known breaking changes to watch (net5 → net10)

| Area | Risk | Mitigation |
| --- | --- | --- |
| **Npgsql timestamp mapping** (Npgsql 6.0+) | **High.** `DateTime` handling for `timestamp without time zone` changed; `DateTime.Kind` is now enforced, and `timestamptz` expects UTC. The `Appointment.StartDate/EndDate` columns are `timestamp without time zone`; values are built with `DateTime.Parse` (unspecified kind). Symptoms: `InvalidCastException` / "Cannot write DateTime with Kind=UTC/Local" on save, or shifted times on read. | Fastest: set `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` at startup to keep .NET 5 semantics. Proper fix (later ADR): normalise to `DateTimeKind.Utc` or move columns to `timestamptz`. Decide in Phase A; document the choice. |
| **Generic host / `IWebHostBuilder` extensions** | Medium | Handled by the Phase B rewrite to `WebApplication`. |
| **Heroku `DATABASE_URL` parser** in `Startup` | Medium | The hand-rolled parser emits `SSL Mode=Require;Trust Server Certificate=True`. Verify these keywords against the Npgsql 10 connection-string parser; adjust casing/names if it throws. Consider `NpgsqlConnectionStringBuilder`. |
| **Identity default UI** | Low | App uses custom `Account` views; `Identity.UI` is still referenced but not routed. Confirm no scaffolded `Areas/Identity` conflict after bump. |
| **`Newtonsoft.Json` version conflicts** | Low/Medium | New Mailjet may pull a different `Newtonsoft` major; watch for `MSB3277`/binding warnings. Resolve by explicit `PackageReference` or by removing Newtonsoft in Phase C. |
| **Session / distributed memory cache** | Low | API unchanged; `IdleTimeout = 10 days` is unusual but preserved. |
| **Nullable / analyzer warnings** | Low (deferred) | Not enabled until Phase D. |
| **C# language version jump (9 → 14)** | Low | Source-compatible; no action. |

---

## 7. Rollback

- Each phase PR is independently revertible; `master` remains on the last-green state.
- No destructive DB step in Phases A–C **unless** the timestamp fix is done as a schema migration — if so,
  that migration gets its own PR with a tested `Down()` and a note here.
- Keep the pre-migration `master` commit SHA recorded in the Phase A PR description.

---

## 8. Acceptance criteria (whole milestone)

- [ ] `dotnet build ApplicationScheduling.sln` → 0 warnings, 0 errors on `net10.0`.
- [ ] `git grep -n "Startup"` returns nothing meaningful; entry point is `Program.cs` only.
- [ ] Only the Npgsql EF provider is referenced.
- [ ] Manual smoke test passes: register, login, create appointment, calendar shows it (correct time),
      doctor confirm turns it green, edit resets to pending, delete removes it, doctor-filter dropdown works.
- [ ] Email path verified once with real Mailjet creds in user-secrets; unconfigured path no-ops with a log.
- [ ] `DECISIONS.md` updated with the timestamp-behaviour decision and any deviations from this plan.
- [ ] No secret value in any commit on any migration branch.

---

## 9. Explicitly out of scope (candidate follow-ups)

- System.Text.Json migration (API responses, any remaining Newtonsoft use).
- Replacing the calendar front-end gap (`script-custom-calendar.js` calls `kendoDateTimePicker` and
  `FullCalendar`, neither of which is vendored under `wwwroot/lib` or `libman.json`).
- Repository/unit-of-work or DTO cleanup in the service layer.
- Container/deployment definition; the `DATABASE_URL` branch suggests a past Heroku deploy — retarget to a
  supported host.
- Test project + meaningful coverage of `AppointmentService`.
