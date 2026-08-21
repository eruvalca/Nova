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

- [ ] Operational runbook covers AppHost startup, resources, and health endpoints.
- [ ] Migration documentation covers creating and applying migrations.
- [ ] Identity no-op behavior is documented.
- [ ] Documentation is linked from the epic's product reference.

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

Status: Not started

- [ ] Create `docs/operational-runbook.md` with a title, one-paragraph purpose/audience, and a
      "Related documents" pointer to `../plans/mvp-product-workflows.md`.
- [ ] Write prerequisites: .NET SDK pinned by `global.json` (10.0.204, `latestFeature` roll-forward);
      first `dotnet run --project Nova.AppHost` may acquire the Aspire CLI bundle (`AspireUseCliBundle`
      delegates through `dnx`).
- [ ] Document startup and resources from `Nova.AppHost/AppHost.cs`: `postgres` container (tag 18,
      data volume) with `novadb` database; `storage` Azure Storage running the Azurite blob emulator
      (data volume) with the `profile-photos` blob container; the `nova` project wired with external
      HTTP endpoints, `WaitFor` both resources, and a `/health` HTTP health check.
- [ ] Document the Aspire dashboard, the health endpoints `/health` and `/alive`
      (`MapDefaultEndpoints()` via ServiceDefaults, `.WithHttpHealthCheck("/health")` on the nova
      resource), and that `/openapi` is Development-only.
- [ ] Document the dashboard resource commands — **Reset nova database** (postgres) and **Clear
      profile photos** (storage), both requiring a `yes` confirmation — plus their CLI equivalents
      (`aspire resource postgres reset-db --confirm yes`, `aspire resource storage clear-profile-photos
      --confirm yes`).
- [ ] Document `aspire stop --force` as the destructive reset that permanently deletes Postgres and
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

_(write when phase completes)_

## Phase 2: Write the "Database migrations" section

Status: Not started

- [ ] Document the single migration set under `Nova/Data/Migrations/`, attributed to `NovaDbContext`,
      and the trap that applying migrations through any other context (`NovaReadDbContext`,
      `NovaAdminDbContext`) silently finds zero migrations — always migrate through `NovaDbContext`.
- [ ] Document creating an incremental migration: the design-time factory
      `NovaDbContextDesignTimeFactory` (`NullCurrentUserProvider`) attaching
      `.UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)`; point to the
      `add-domain-persistence` skill for the exact command form.
- [ ] Document the pending-changes check:
      `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext`.
- [ ] Document the Identity `SchemaVersion = IdentitySchemaVersions.Version3` caveat: any context
      built outside the host (design-time factory, scripts, test harnesses) must attach
      `UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)` or `MigrateAsync`
      throws `PendingModelChangesWarning`.
- [ ] Document runtime application: `StartupDatabaseInitializer` runs `MigrateAsync` **only in
      Development** and seeds the `Admin`/`ClubAdmin`/`StandardUser` roles in **all** environments,
      both through the execution strategy.
- [ ] Document the SDK pin (`global.json`: 10.0.204, `latestFeature`) and the CI boundary
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

_(write when phase completes)_

## Phase 3: Write the "Identity and email" section

Status: Not started

- [ ] Document the no-op email sender: `IEmailSender<NovaUserEntity>` is registered as
      `IdentityNoOpEmailSender` (singleton, `Nova/Components/Account/IdentityNoOpEmailSender.cs`);
      no emails are delivered.
- [ ] Document `SignIn.RequireConfirmedAccount = false` and that no external login providers are
      registered; registration confirmation, password reset, and all other email flows are no-ops.
- [ ] Document the `RegisterConfirmation` special case: the page checks for
      `IdentityNoOpEmailSender` and shows the no-email confirmation path; the sender's comment notes
      to remove that `else if` block when a real implementation is added.
- [ ] State the operator implications explicitly: operators must not assume email delivery, account
      confirmation, or password-reset email exists; account recovery requires direct database/user
      management until a real sender is introduced.

### Verification Plan

- Grep-verify each claim against `Nova/Program.cs` (identity options + sender registration),
      `Nova/Components/Account/IdentityNoOpEmailSender.cs`, and
      `Nova/Components/Account/Pages/RegisterConfirmation.razor.cs`.
- Confirm the section contains the "must not assume" implications wording required by the issue
      scope ("so operators don't assume email flows").

### Phase Summary

_(write when phase completes)_

## Phase 4: Write the "Operational caveats" section

Status: Not started

- [ ] Browser-suite prerequisites: one-time per-machine
      `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`; relocate the browser
      cache with `PLAYWRIGHT_BROWSERS_PATH` if needed.
- [ ] Env-gated browser evidence: `NOVA_BROWSER_HEADED=1` shows the browser;
      `NOVA_A11Y_SCREENSHOTS=1` captures accessibility evidence (screenshots + contrast/touch-target
      measurements) to `%TEMP%\nova-a11y-screenshots`.
- [ ] Integration and browser suites are local-only: CI runs build and unit tests only; run both
      suites locally before merge.
- [ ] Observability note from #120: ProblemDetails `traceId` correlates with the client-sent
      `traceparent` for service problems, framework 400s, and status-code pages; the
      500/unhandled-exception producer is explicitly untested (no fault-injection surface), so
      trace IDs on 500s should be treated as unverified.
- [ ] Re-check epic #13 "Current readiness" and the other hardening children for any operational
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

_(write when phase completes)_

## Phase 5: Link from the product reference and final acceptance pass

Status: Not started

- [ ] Add a short "Operational documentation" section to `plans/mvp-product-workflows.md` with a
      relative link to `../docs/operational-runbook.md`.
- [ ] Walk the four acceptance criteria against the finished runbook; fix any gaps.
- [ ] Re-run the Phase 1–4 verification batteries once against the final doc text.
- [ ] Run `dotnet format Nova.slnx --verify-no-changes` (must pass — no C# files changed).
- [ ] Confirm `git diff --stat` shows only markdown changes
      (`docs/operational-runbook.md`, `plans/mvp-product-workflows.md`,
      `plans/operational-migration-documentation.md`) and no production behavior change.
- [ ] Commit the plan + runbook + link with a docs-only commit message and the
      `Co-authored-by` trailer.

### Verification Plan

- The relative link in `plans/mvp-product-workflows.md` resolves to the existing
      `docs/operational-runbook.md`.
- Acceptance checklist above fully checked.
- `dotnet format Nova.slnx --verify-no-changes` exits 0.
- `git status` / `git diff --stat` restricted to the three markdown files.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions — for this docs-only issue,
expected to be: verify format/acceptance criteria, open PR against main linked to #122, merge)_
