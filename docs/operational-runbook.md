# Nova MVP Operational Runbook

This document is the operator-facing reference for running, migrating, and supporting the Nova MVP
after delivery. Its audience is anyone who needs to boot the app, apply or verify EF Core migrations,
understand the Identity email behavior, or interpret the operational caveats surfaced by the
hardening waves. Every command, path, and behavior statement here is copy-paste verifiable against
the repository; file:line citations point to the authoritative source for each claim.

Related documents: [`../plans/mvp-product-workflows.md`](../plans/mvp-product-workflows.md) — the
epic #13 product reference that links to this runbook.

## 1. Running the app locally

### Prerequisites

- The .NET SDK is pinned by `global.json` to version `10.0.204` with `rollForward: "latestFeature"`
  (`global.json:2-5`). `dotnet --version` should report `10.0.204` or a later `10.0` feature band.

### Startup

- `dotnet run --project Nova.AppHost` is the supported manual developer entry point.
  `Nova.AppHost/Nova.AppHost.csproj` sets `<AspireUseCliBundle>true</AspireUseCliBundle>`
  (`Nova.AppHost/Nova.AppHost.csproj:6`), which delegates the run to `aspire run` through `dnx`; the
  first run on a machine may acquire the Aspire CLI bundle before the dashboard opens
  (`.github/instructions/testing.instructions.md:44-47`).
- Agents and automation use `aspire start --isolated --non-interactive` instead
  (`.github/instructions/testing.instructions.md:47`).
- `Nova` has no usable database connection string or blob client without the AppHost — always boot
  through `Nova.AppHost` (`.github/copilot-instructions.md:14`).

### Resources (`Nova.AppHost/AppHost.cs`)

- `postgres` — PostgreSQL 18 container (`WithImageTag("18")`) with a persistent data volume
  (`WithDataVolume()`), plus a `nova` database exposed as the `novadb` connection string/resource
  (`postgres.AddDatabase("novadb", "nova")`) (`Nova.AppHost/AppHost.cs:5-10`). The `nova` project
  reads it as the `novadb` connection string (`GetConnectionString("novadb")` at `Nova/Program.cs:139`).
- `storage` — Azure Storage running the Azurite blob emulator (`RunAsEmulator`) with a persistent
  data volume, exposing the `profile-photos` blob container (`AddBlobContainer("profile-photos")`)
  (`Nova.AppHost/AppHost.cs:12-16`).
- `nova` — the Blazor server project (`AddProject<Projects.Nova>("nova")`), wired to
  `WithReference` + `WaitFor` both `novadb` and `profile-photos`, with external HTTP endpoints
  (`WithExternalHttpEndpoints()`) and an HTTP health check at `/health` (`WithHttpHealthCheck("/health")`)
  (`Nova.AppHost/AppHost.cs:18-25`).

### Aspire dashboard and health endpoints

- The Aspire dashboard is exposed when the AppHost runs. The VS Code Aspire extension no longer opens
  it automatically; opt in with its `dashboardBrowser` setting or configure dashboard launch behavior
  in `launch.json` (`.github/instructions/testing.instructions.md:56-57`).
- `/health` and `/alive` are mapped by `app.MapDefaultEndpoints()` (`Nova/Program.cs:197`), which
  lives in `Nova.ServiceDefaults/Extensions.cs`. Both are registered **only in the Development
  environment** (`Nova.ServiceDefaults/Extensions.cs:112-122`):
  - `/health` — all health checks must pass (readiness).
  - `/alive` — only checks tagged `live` must pass (liveness).
- `.WithHttpHealthCheck("/health")` (`Nova.AppHost/AppHost.cs:25`) makes Aspire poll `/health` to
  report the `nova` resource's health in the dashboard.
- `/openapi` is Development-only (`Nova/Program.cs:200-205`, `app.MapOpenApi()`).

### Dashboard resource commands and destructive resets

The dashboard exposes two destructive commands, both gated behind a required `yes` confirmation
(`Nova.AppHost/AppHost.cs:27-45`):

| Command | Resource | Effect | CLI equivalent |
| --- | --- | --- | --- |
| **Reset nova database** | `postgres` | Drops and recreates the nova database, then restarts Nova so migrations run again | `aspire resource postgres reset-db --confirm yes` |
| **Clear profile photos** | `storage` | Deletes every blob from the `profile-photos` container | `aspire resource storage clear-profile-photos --confirm yes` |

- `aspire stop --force` is the broader destructive reset: it permanently deletes the Postgres and
  Azurite volume data. Prefer the targeted commands above when only the database or profile photos
  need resetting (`.github/instructions/testing.instructions.md:53-55`).
- Stop the AppHost cleanly with `aspire stop` (not `--force`) when a reset is not intended.

## 2. Database migrations

### One migration set, attributed to `NovaDbContext`

- There is a single migration set under `Nova/Data/Migrations/` — **15 migrations** as of this
  writing — all attributed `[DbContext(typeof(NovaDbContext))]`
  (`.github/instructions/ef-core-tenancy.instructions.md:88-92`).
- **Trap:** because the migrations are attributed to `NovaDbContext`, applying them through any other
  context (`NovaReadDbContext`, `NovaAdminDbContext`) via `Database.MigrateAsync()` silently finds
  **zero** migrations. Always migrate through `NovaDbContext`
  (`.github/instructions/ef-core-tenancy.instructions.md:90-92`).

### Creating an incremental migration

- Design-time tooling uses `NovaDbContextDesignTimeFactory`
  (`IDesignTimeDbContextFactory<NovaDbContext>`), which builds the context with
  `NullCurrentUserProvider` and attaches `.UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)`
  (`Nova/Data/NovaDbContextDesignTimeFactory.cs:18-26`).
- Generate against `NovaDbContext` (recipe from the `add-domain-persistence` skill):

  ```powershell
  dotnet ef migrations add <Name> --project Nova --context NovaDbContext
  ```

- Inspect `Up`, `Down`, and the model snapshot; document any intentional destructive cleanup.

### Pending-changes check

```powershell
dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext
```

Expected output: `No changes have been made to the model since the last migration.`

### Identity schema-version caveat

- The runtime sets `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3`
  (`Nova/Program.cs:163`), which adds the .NET 10 `AspNetUserPasskeys` table (visible in
  `Nova/Data/Migrations/20260610050939_InitialCreate.cs:253`).
- Identity reads that option from the **application service provider** while building the model. Any
  context built outside the host (design-time factory, test harnesses, scripts) **must** attach
  `.UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)` or its model silently
  differs from the migrations — at runtime this surfaces as a `PendingModelChangesWarning` thrown by
  `MigrateAsync` (`.github/instructions/ef-core-tenancy.instructions.md:93-99`).
- `IdentityStoreServiceProvider.Instance` (`Nova/Data/IdentityStoreServiceProvider.cs:12-20`) is a
  minimal service provider that pins the same `Version3` for exactly this purpose.

### Runtime application

- `Nova/Program.cs:287` calls
  `StartupDatabaseInitializer.InitializeAsync(app.Services, app.Environment.IsDevelopment())`.
- `StartupDatabaseInitializer` (`Nova/Data/Startup/StartupDatabaseInitializer.cs:21-43`):
  - runs `MigrateAsync` **only when `applyMigrations` is `true`** (i.e. Development only);
  - seeds the `Admin`, `ClubAdmin`, and `StandardUser` roles in **all** environments
    (`StartupDatabaseInitializer.cs:53`);
  - both through `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`
    (`StartupDatabaseInitializer.cs:33-42`).

### SDK pin and CI boundary

- SDK is pinned in `global.json` (`10.0.204`, `latestFeature`).
- CI (`.github/workflows/ci.yml`) runs **restore + build + unit tests only** on `ubuntu-latest` with
  .NET `10.0.x` (`ci.yml:9-43`). It does **not** run integration or browser tests.
- The integration (`Nova.Integration.Tests`, needs the AppHost's real PostgreSQL 18) and browser
  (`Nova.Browser.Tests`) suites run locally, not in CI
  (`.github/instructions/testing.instructions.md:39`).

## 3. Identity and email (no-op)

- `IEmailSender<NovaUserEntity>` is registered as a **singleton** `IdentityNoOpEmailSender`
  (`Nova/Program.cs:176`).
- `IdentityNoOpEmailSender` (`Nova/Components/Account/IdentityNoOpEmailSender.cs:14-49`) delegates to
  the framework `NoOpEmailSender` — no emails are ever delivered. Its `<remarks>` note to remove the
  `else if (EmailSender is IdentityNoOpEmailSender)` block once a real sender is added
  (`IdentityNoOpEmailSender.cs:11-13`).
- `SignIn.RequireConfirmedAccount = false` (`Nova/Program.cs:162`), and no external login providers
  are registered (Identity is configured via `AddIdentityCookies()` plus `AddIdentityCore` →
  `AddEntityFrameworkStores<NovaAdminDbContext>()` → `AddDefaultTokenProviders()`; there is no Google/
  Facebook/etc. provider) (`Nova/Program.cs:61-66,160-169`).
- `RegisterConfirmation` has a no-email special case: when `emailSender is IdentityNoOpEmailSender`,
  it generates and displays a confirmation link in-page instead of emailing it
  (`Nova/Components/Account/Pages/RegisterConfirmation.razor.cs:69-78`).

**Operator implications:** do **not** assume email delivery, account confirmation, or password-reset
email exists. Registration confirmation and password reset are no-ops at the transport layer, and
account recovery requires direct database/user management until a real email sender is introduced.

## 4. Operational caveats

### Browser suite prerequisites

- One-time per-machine setup: `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`
  (relocate the browser cache with `PLAYWRIGHT_BROWSERS_PATH` if needed)
  (`.github/instructions/testing.instructions.md:80-81`).

### Env-gated browser evidence

- `NOVA_BROWSER_HEADED=1` shows the browser during the browser suite.
- `NOVA_A11Y_SCREENSHOTS=1` captures accessibility evidence (screenshots + contrast/touch-target
  measurements) to `%TEMP%\nova-a11y-screenshots`. In code this is
  `Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots")` (e.g.
  `Nova.Browser.Tests/CampaignEvaluationBrowserTests.cs:487`).
- These helpers `Assert.Skip(...)` when their flag is unset so a green run always means the assertions
  executed (`.github/instructions/testing.instructions.md:82-84`).

### Integration and browser suites are local-only

- CI runs build and unit tests only; run both `Nova.Integration.Tests` and `Nova.Browser.Tests`
  locally before merge (`.github/instructions/testing.instructions.md:39`).
- `Nova.Integration.Tests` requires the Aspire AppHost's real PostgreSQL 18; `Nova.Browser.Tests`
  requires the AppHost plus Playwright Chromium.

### Observability (from #120)

- `ProblemDetails` responses surface a `traceId` from `Activity.Current?.TraceId`
  (`Nova/Program.cs:180-190`). This correlates with the client-sent `traceparent` (W3C trace context)
  for service problems, framework 400s, and status-code pages.
- The 500/unhandled-exception producer has no fault-injection surface and is explicitly untested, so
  trace IDs on 500s should be treated as **unverified** — do not rely on them for correlation until
  they are covered.
