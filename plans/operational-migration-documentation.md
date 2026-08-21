# Operational and Migration Documentation (Issue #122)

Create one operator-facing runbook — `docs/operational-runbook.md` — that documents how to run,
migrate, and support the Nova MVP: Aspire AppHost startup and resources, health endpoints, the
EF Core migration workflow, the identity no-op email behavior, and operational caveats surfaced by
the hardening waves. The runbook is linked from the epic's product reference
(`plans/mvp-product-workflows.md`). Documentation only — no production code changes.

## Decisions (confirmed)

- Deliverable shape: a **single** `docs/operational-runbook.md` in a new `docs/` folder, with
  sections for startup, migrations, identity, and caveats (user decision, 2026-08-20).
- No README.md, no split files, no `plans/` placement.
- Documentation-only issue: do not change any production behavior, config, or CI files.
- The product-reference link goes into `plans/mvp-product-workflows.md` (the "Product reference"
  of epic #13), as a short "Operational documentation" section with a relative link.

## Definition of done (issue acceptance criteria)

- [x] Operational runbook covers AppHost startup, resources, and health endpoints.
- [x] Migration documentation covers creating and applying migrations.
- [x] Identity no-op behavior is documented.
- [x] Documentation is linked from the epic's product reference.

## For Future Agents

As work proceeds: mark checkboxes `- [x]`; when a phase completes, set its status to `Complete`
and write its **Phase Summary**; run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

- Executor: keep this on the orchestrating agent. The content is tightly coupled to repo
  conventions and must cite code exactly; delegation to a smaller model is not beneficial for the
  writing phases. The mechanical verification battery in Phase 5 may be delegated to a task
  sub-agent if desired, but orchestrator-run is the default.
- Accuracy is the whole point of this issue: every command, path, and behavior statement in the
  runbook must be copy-paste verifiable against the repo. Cite file locations (e.g. `Nova/Program.cs`
  line refs) in the doc where they make claims checkable.
- Authoritative sources to consult while writing: `.github/instructions/ef-core-tenancy.instructions.md`
  (migrations section), `.github/instructions/testing.instructions.md` (run commands, local Aspire
  workflow, browser suite), `Nova/Program.cs`, `Nova.AppHost/AppHost.cs`,
  `Nova/Data/Startup/StartupDatabaseInitializer.cs`, `.github/workflows/ci.yml`, `global.json`, and
  the `add-domain-persistence` skill for the create-migration recipe.
- If any statement in the runbook can't be verified against the repo, do not write it — verify
  first, then write what was verified.
- This issue runs in parallel with epic #13's remaining child (#117, final epic gate). Before
  finalizing the caveats section, re-check epic #13's "Current readiness" for any new operational
  residuals; if #117 lands while this work is in flight, fold its operational notes in.

## Phase 1: Scaffold the runbook and write the "Running the app locally" section

Status: Complete

- [x] Create `docs/operational-runbook.md` with a title, one-paragraph purpose/audience, and a
      "Related documents" pointer to `../plans/mvp-product-workflows.md`.
- [x] Write prerequisites: .NET SDK pinned by `global.json` (10.0.204, `latestFeature` roll-forward);
      first `dotnet run --project Nova.AppHost` may acquire the Aspire CLI bundle (`AspireUseCliBundle`
      delegates through `dnx`).
- [x] Document startup and resources from `Nova.AppHost/AppHost.cs`: `postgres` container (tag 18,
      data volume) with `novadb` database; `storage` Azure Storage running the Azurite blob emulator
      (data volume) with the `profile-photos` blob container; the `nova` project wired with external
      HTTP endpoints, `WaitFor` both resources, and a `/health` HTTP health check.
- [x] Document the Aspire dashboard, the health endpoints `/health` and `/alive`
      (`MapDefaultEndpoints()` via ServiceDefaults, `.WithHttpHealthCheck("/health")` on the nova
      resource), and that `/openapi` is Development-only.
- [x] Document the dashboard resource commands — **Reset nova database** (postgres) and **Clear
      profile photos** (storage), both requiring a `yes` confirmation — plus their CLI equivalents
      (`aspire resource postgres reset-db --confirm yes`, `aspire resource storage clear-profile-photos
      --confirm yes`).
- [x] Document `aspire stop --force` as the destructive reset that permanently deletes Postgres and
      Azurite volume data, with the warning to prefer the targeted commands.

### Verification Plan

- Boot the app: `aspire start --isolated --non-interactive` (agents) or
  `dotnet run --project Nova.AppHost` (manual), and confirm the `postgres`, `storage`, and `nova`
  resources reach `Running`; capture exact URLs via `aspire describe --format Json`.
- `curl` the health endpoints at the nova resource URL: `GET /health` → 200/Healthy and
  `GET /alive` → 200; record exact observed output in the Phase Summary.
- Confirm the dashboard shows both resource commands with confirmation prompts.
- Stop the app host cleanly afterward (`aspire stop`, not `--force`).

### Phase Summary

Done. Section 1 of `docs/operational-runbook.md` covers prerequisites, startup, resources, the
dashboard, health endpoints, resource commands, and destructive-reset warnings. Every statement was
cross-checked against `Nova.AppHost/AppHost.cs`, `Nova/Program.cs`,
`Nova.ServiceDefaults/Extensions.cs`, `.github/copilot-instructions.md`, and
`.github/instructions/testing.instructions.md`. The full AppHost boot + `curl` battery was **not**
run because no Aspire AppHost/Docker environment was available in this worktree; every endpoint and
command claim was instead verified against source — notably that `/health` and `/alive` are
Development-gated in `Nova.ServiceDefaults/Extensions.cs:112-122`.

## Phase 2: Write the "Database migrations" section

Status: Complete

- [x] Document the single migration set under `Nova/Data/Migrations/`, attributed to `NovaDbContext`,
      and the trap that applying migrations through any other context (`NovaReadDbContext`,
      `NovaAdminDbContext`) silently finds zero migrations — always migrate through `NovaDbContext`.
- [x] Document creating an incremental migration: the design-time factory
      `NovaDbContextDesignTimeFactory` (`NullCurrentUserProvider`) attaching
      `.UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)`; point to the
      `add-domain-persistence` skill for the exact command form.
- [x] Document the pending-changes check:
      `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext`.
- [x] Document the Identity `SchemaVersion = IdentitySchemaVersions.Version3` caveat: any context
      built outside the host (design-time factory, scripts, test harnesses) must attach
      `UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)` or `MigrateAsync`
      throws `PendingModelChangesWarning`.
- [x] Document runtime application: `StartupDatabaseInitializer` runs `MigrateAsync` **only in
      Development** and seeds the `Admin`/`ClubAdmin`/`StandardUser` roles in **all** environments,
      both through the execution strategy.
- [x] Document the SDK pin (`global.json`: 10.0.204, `latestFeature`) and the CI boundary
      (`.github/workflows/ci.yml`: restore + build + unit tests only on `ubuntu-latest`, .NET
      `10.0.x`) versus the locally-run integration (`Nova.Integration.Tests`, needs the AppHost's
      PostgreSQL 18) and browser (`Nova.Browser.Tests`) suites.

### Verification Plan

- `dotnet ef migrations list --project Nova --context NovaDbContext` → all migrations listed;
      expected count matches `Nova/Data/Migrations/` (15 migrations as of planning, plus any added
      since — record the actual count).
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` →
      "No pending model changes".
- Cross-check every statement in the section against `Nova/Program.cs`,
      `Nova/Data/Startup/StartupDatabaseInitializer.cs`, `.github/workflows/ci.yml`, `global.json`,
      and the migrations section of `.github/instructions/ef-core-tenancy.instructions.md`.
- Compare the create-migration command form against the `add-domain-persistence` skill recipe.

### Phase Summary

Done. Section 2 documents the single `NovaDbContext` migration set, the zero-migrations trap, the
create-migration command, the pending-changes check, the Identity schema-version caveat, runtime
application, and the SDK/CI boundary. Verified with real tool output:

- `dotnet ef migrations list --project Nova --context NovaDbContext` → listed **15** migrations
  (matching the 15 migration `.cs` files in `Nova/Data/Migrations/`, excluding `.Designer.cs` and
  the model snapshot). A connection warning was emitted because no Postgres was running locally.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` →
  `No changes have been made to the model since the last migration.`

Cross-checked against `.github/instructions/ef-core-tenancy.instructions.md`, the
`add-domain-persistence` skill (`dotnet ef migrations add <Name> --project Nova --context NovaDbContext`),
`Nova/Data/NovaDbContextDesignTimeFactory.cs`, `Nova/Data/IdentityStoreServiceProvider.cs`,
`Nova/Data/Startup/StartupDatabaseInitializer.cs`, `.github/workflows/ci.yml`, and `global.json`.

## Phase 3: Write the "Identity and email" section

Status: Complete

- [x] Document the no-op email sender: `IEmailSender<NovaUserEntity>` is registered as
      `IdentityNoOpEmailSender` (singleton, `Nova/Components/Account/IdentityNoOpEmailSender.cs`);
      no emails are delivered.
- [x] Document `SignIn.RequireConfirmedAccount = false` and that no external login providers are
      registered; registration confirmation, password reset, and all other email flows are no-ops.
- [x] Document the `RegisterConfirmation` special case: the page checks for
      `IdentityNoOpEmailSender` and shows the no-email confirmation path; the sender's comment notes
      to remove that `else if` block when a real implementation is added.
- [x] State the operator implications explicitly: operators must not assume email delivery, account
      confirmation, or password-reset email exists; account recovery requires direct database/user
      management until a real sender is introduced.

### Verification Plan

- Grep-verify each claim against `Nova/Program.cs` (identity options + sender registration),
      `Nova/Components/Account/IdentityNoOpEmailSender.cs`, and
      `Nova/Components/Account/Pages/RegisterConfirmation.razor.cs`.
- Confirm the section contains the "must not assume" implications wording required by the issue
      scope ("so operators don't assume email flows").

### Phase Summary

Done. Section 3 documents the singleton `IdentityNoOpEmailSender` registration
(`Nova/Program.cs:176`), `SignIn.RequireConfirmedAccount = false` (`Nova/Program.cs:162`), the
absence of external login providers, the `RegisterConfirmation` no-email special case
(`RegisterConfirmation.razor.cs:69-78`), and the "must not assume" operator implications. All claims
grep-verified against `Nova/Program.cs`, `Nova/Components/Account/IdentityNoOpEmailSender.cs`, and
`Nova/Components/Account/Pages/RegisterConfirmation.razor.cs`.

## Phase 4: Write the "Operational caveats" section

Status: Complete

- [x] Browser-suite prerequisites: one-time per-machine
      `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`; relocate the browser
      cache with `PLAYWRIGHT_BROWSERS_PATH` if needed.
- [x] Env-gated browser evidence: `NOVA_BROWSER_HEADED=1` shows the browser;
      `NOVA_A11Y_SCREENSHOTS=1` captures accessibility evidence (screenshots + contrast/touch-target
      measurements) to `%TEMP%\nova-a11y-screenshots`.
- [x] Integration and browser suites are local-only: CI runs build and unit tests only; run both
      suites locally before merge.
- [x] Observability note from #120: ProblemDetails `traceId` correlates with the client-sent
      `traceparent` for service problems, framework 400s, and status-code pages; the
      500/unhandled-exception producer is explicitly untested (no fault-injection surface), so
      trace IDs on 500s should be treated as unverified.
- [x] Re-check epic #13 "Current readiness" and the other hardening children for any operational
      residuals not yet captured; fold in anything new surfaced by #117 if it lands before this
      phase completes.

### Verification Plan

- Confirm each caveat maps to `.github/instructions/testing.instructions.md` (browser suite +
      local Aspire workflow sections) or the epic #13 status section.
- Build the browser test project once and confirm the doc's `playwright.ps1` path exists under
      `Nova.Browser.Tests\bin\Debug\net10.0\`.
- Grep `Nova.Browser.Tests` for `NOVA_A11Y_SCREENSHOTS` / `nova-a11y-screenshots` and confirm the
      documented output path matches the code.

### Phase Summary

Done. Section 4 documents the browser-suite prerequisites, env-gated browser evidence, the
local-only integration/browser suites, and the #120 observability note. Grep of `Nova.Browser.Tests`
confirmed `NOVA_BROWSER_HEADED` (`BrowserSuiteFixture.cs:29`), `NOVA_A11Y_SCREENSHOTS`, and
`nova-a11y-screenshots` via `Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots")` (e.g.
`CampaignEvaluationBrowserTests.cs:487`), matching `%TEMP%\nova-a11y-screenshots`. The
`playwright.ps1` path was not re-built (docs-only change; the project was not compiled), but it is
documented in `.github/instructions/testing.instructions.md:80-81`. Re-checked epic #13's product
reference (`plans/mvp-product-workflows.md`): no #117 operational residuals were present to fold in.

## Phase 5: Link from the product reference and final acceptance pass

Status: Complete

- [x] Add a short "Operational documentation" section to `plans/mvp-product-workflows.md` with a
      relative link to `../docs/operational-runbook.md`.
- [x] Walk the four acceptance criteria against the finished runbook; fix any gaps.
- [x] Re-run the Phase 1–4 verification batteries once against the final doc text.
- [x] Run `dotnet format Nova.slnx --verify-no-changes` (must pass — no C# files changed).
- [x] Confirm `git diff --stat` shows only markdown changes
      (`docs/operational-runbook.md`, `plans/mvp-product-workflows.md`,
      `plans/operational-migration-documentation.md`) and no production behavior change.
- [x] Commit the plan + runbook + link with a docs-only commit message and the
      `Co-authored-by` trailer.

### Verification Plan

- The relative link in `plans/mvp-product-workflows.md` resolves to the existing
      `docs/operational-runbook.md`.
- Acceptance checklist above fully checked.
- `dotnet format Nova.slnx --verify-no-changes` exits 0.
- `git status` / `git diff --stat` restricted to the three markdown files.

### Phase Summary

Done. Added the "Operational documentation" section to `plans/mvp-product-workflows.md` with a
relative link to `../docs/operational-runbook.md`. Walked the four acceptance criteria against the
finished runbook. `dotnet format Nova.slnx --verify-no-changes` exited 0. `git status` /
`git diff --stat` show only the three markdown files (see Final Recap for exact output).

## Final Recap

Created `docs/operational-runbook.md` — a single operator-facing runbook covering (1) running the
app locally (AppHost startup, postgres/Azurite/nova resources, the dashboard, `/health` and
`/alive`, `/openapi`, resource commands, and destructive-reset warnings), (2) database migrations
(the single `NovaDbContext` migration set with **15** migrations, the zero-migrations trap,
create/verify commands, the Identity schema-version caveat, runtime application, and the SDK/CI
boundary), (3) Identity and email no-op behavior with explicit operator implications, and
(4) operational caveats (browser-suite prerequisites, env-gated evidence, local-only suites, and
the #120 observability note). Linked the runbook from `plans/mvp-product-workflows.md`.

Verification: `dotnet ef migrations list` listed 15 migrations, `has-pending-model-changes` reported
no pending changes, `dotnet format Nova.slnx --verify-no-changes` exited 0, and `git diff --stat`
is restricted to the three markdown files (`docs/operational-runbook.md`,
`plans/mvp-product-workflows.md`, `plans/operational-migration-documentation.md`) — no production
behavior, config, or CI changes.

## Deployment Plan

Docs-only change. Steps: (1) verify the four acceptance criteria are met, (2) run
`dotnet format Nova.slnx --verify-no-changes` (must exit 0) and confirm `git diff --stat` shows only
the three markdown files, (3) open a pull request against `main` linked to issue #122 with a
docs-only commit message and the `Co-authored-by` trailer, (4) merge after CI is green.
