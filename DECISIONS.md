# Decisions Log

A running record of architecture and process decisions for the Appointment Scheduler.
Newest entries at the top. Each entry: **Context → Decision → Consequences**. Keep entries short;
link to code or the migration plan for detail. Do not rewrite past entries — supersede them with a new one.

---

## ADR-0007 — Branch and PR conventions

**Date:** 2026-09-10 · **Status:** Accepted

**Context.** The repo had a single `master` branch, direct commits, no `.gitignore`, and 457 build-artifact
files under source control. There is a GitHub remote (`bhargavkoya/AppointmentScheduler`).

**Decision.**
- All work happens on a branch off `master` and lands via PR. No direct pushes to `master`.
- Branch prefixes: `feat/`, `fix/`, `chore/`, `docs/`, and `migration/net10-<step>` for the upgrade.
- Each PR states what changed and how it was verified.
- `.gitignore` added; `bin/`, `obj/`, `.vs/`, `*.user` removed from tracking.

**Consequences.** Contributors must `git pull` after the artifact-removal PR merges (their local copies keep
untracked `bin/obj`, which is harmless). History is **not** rewritten (see ADR-0003).

---

## ADR-0006 — Keep PostgreSQL; drop the unused SQL Server EF provider

**Date:** 2026-09-10 · **Status:** Accepted (implementation deferred to the migration)

**Context.** `ApplicationScheduling.csproj` references both `Microsoft.EntityFrameworkCore.SqlServer` and
`Npgsql.EntityFrameworkCore.PostgreSQL`. Only Npgsql is wired up (`options.UseNpgsql(...)` in `Startup`), and
the single migration (`20220614141724_PostgresAdded`) is Npgsql-specific.

**Decision.** Standardise on PostgreSQL. Remove the `Microsoft.EntityFrameworkCore.SqlServer` package
reference during the .NET 10 migration.

**Consequences.** Smaller dependency surface. If SQL Server support is ever needed it must be added back
deliberately with its own provider registration.

---

## ADR-0005 — Convert `Startup.cs` to the minimal hosting model

**Date:** 2026-09-10 · **Status:** Accepted (implementation deferred to the migration)

**Context.** The app uses the .NET 3.x/5 `Program.cs` + `Startup.cs` split. .NET 6+ templates use a single
`Program.cs` with `WebApplicationBuilder`. The split is still supported in .NET 10 but is off the mainline
path and complicates future guidance.

**Decision.** Merge `Startup.ConfigureServices` / `Startup.Configure` into `Program.cs` using
`WebApplication.CreateBuilder` as part of the migration. Behaviour (middleware order, DI registrations,
`DbInitializer` invocation) is preserved exactly.

**Consequences.** One-time diff churn in the app's entry point. Easier to follow current ASP.NET Core docs
afterwards.

---

## ADR-0004 — Externalise secrets going forward; rotate exposed keys; do not rewrite history

**Date:** 2026-09-10 · **Status:** Accepted

**Context.** Live-looking secrets were committed and are present in `git log`:
- Mailjet API key + secret — `Utility/EmailSender.cs`
- PostgreSQL password (`Pa$$w0rd`) — `appsettings.json`
- A seed admin password and a stray plaintext `PasswordHash` assignment — `DbInitializer/DbInitializer.cs`

**Decision.**
- All secret/PII values move to configuration keys with **empty placeholders** in `appsettings.json`; real
  values come from **user-secrets** (local) and **environment variables** (deployment). Keys:
  `ConnectionStrings:DefaultConnection`, `Mailjet:ApiKey|ApiSecret|FromEmail|FromName`,
  `Notifications:AdminEmail`, `SeedAdmin:Email|Name|Password`.
- `EmailSender` no-ops with a warning when Mailjet is unconfigured; `DbInitializer` skips admin seeding when
  `SeedAdmin:Password` is unset. The stray `PasswordHash = "Admin@123"` line is removed (it was a bug — it
  set a plaintext hash before `CreateAsync` hashed the real password).
- Git history is **left as-is** (no `filter-repo` / force-push). Instead, **the exposed Mailjet key must be
  rotated in the Mailjet dashboard by the repo owner**, and the old DB password must not be reused.
- `.gitignore` now excludes `secrets.json`, `appsettings.*.local.json`, `.env*`, `*.pfx`, `*.key`.

**Consequences.** The old secrets remain retrievable from history until/unless a future decision rewrites it;
rotation is what actually closes the exposure. Local setup now requires a one-time `dotnet user-secrets`
step (documented in `CLAUDE.md`).

**Follow-up (owner action required):** rotate Mailjet API credentials; confirm the `Pa$$w0rd` DB account is
retired.

---

## ADR-0003 — Git history is not rewritten to purge secrets

**Date:** 2026-09-10 · **Status:** Accepted (revisit if required by policy)

**Context.** Purging secrets from history means `git filter-repo`/BFG + force-pushing `master`, invalidating
every existing clone and any open PRs/forks.

**Decision.** Accept the residual exposure in history and rely on key rotation (ADR-0004) instead. Revisit
only if a security policy or the platform requires scrubbed history.

**Consequences.** `git log -p` still shows the old keys. This is documented and considered mitigated by
rotation.

---

## ADR-0002 — Upgrade target is .NET 10 (LTS), in one hop from .NET 5

**Date:** 2026-09-10 · **Status:** Accepted

**Context.** The project targets `net5.0`, which is out of support (no security fixes). .NET 10 is the
current LTS. Intermediate TFMs (6/7/8/9) offer no value to stop at for an app this small, and the SDK for
10 is already installed on the dev machine.

**Decision.** Retarget straight to `net10.0`. Bump all `Microsoft.*` / EF Core / Npgsql packages to their
10.x lines. Scope is "Moderate" (see the migration plan): retarget + package bumps + fix breaks + minimal
hosting (ADR-0005) + drop unused SqlServer (ADR-0006) + `.gitignore`. Deferred (possible later ADRs):
System.Text.Json instead of Newtonsoft, nullable reference types, removing blocking `.Result` calls, adding
a test project.

**Consequences.** Larger single jump, but far less total work than staged upgrades. Detailed steps and risks
live in `docs/dotnet-10-migration-plan.md`.

---

## ADR-0001 — Adopt this decisions log

**Date:** 2026-09-10 · **Status:** Accepted

**Context.** Non-obvious choices (why Postgres despite a SQL Server package, why `Startup.cs` still exists,
what to do about committed secrets) were undocumented, forcing re-discovery from code and history.

**Decision.** Keep `DECISIONS.md` at the repo root as an append-only ADR log. Record any choice a future
reader could not infer from the code alone. `CLAUDE.md` links here.

**Consequences.** One more file to maintain; PRs that make structural changes should add an entry.
