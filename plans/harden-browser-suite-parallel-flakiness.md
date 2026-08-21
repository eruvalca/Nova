# Harden the Browser Suite Against Parallel-Run Flakiness

Make the committed Playwright browser suite deterministic under its configured 4-way parallel
Chromium load (issue #130) by (1) centralizing the duplicated SSR-hydration retry helpers into one
environment-tunable policy with raised defaults, and (2) hardening the shared seeding path against
transient Azurite connection refusals. Out of scope: product behavior changes, new browser
scenarios, and the env-gated `NOVA_A11Y_SCREENSHOTS` evidence suite.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status
to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to
continue with zero context); run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Decisions locked with the requester before planning:
- Azurite hardening lives in **shared infrastructure** (`NovaAppHostFixture` +
  `IdentityHttpClientHelper`), not browser-suite-only wrappers. Retry only triggers on transient
  failures, so the green 356/356 integration suite is unaffected except for becoming more robust.
- Acceptance requires **3 consecutive green** full-suite runs under the default `MaxThreads = 4`.
- Retry windows become **tunable AND the defaults are raised** (40 → 60 attempts), deduplicating the
  4 per-file copies of `ActUntilAsync`/`ClickUntilAsync` into one shared helper.

Key files (current state):
- Retry helper copies (all hard-code `for (attempt < 40)` + `WaitForTimeoutAsync(250)`):
  `Nova.Browser.Tests\CampaignCloseoutBrowserTests.cs:514-545`,
  `Nova.Browser.Tests\TeamFormBrowserTests.cs:202-216`,
  `Nova.Browser.Tests\PlayerFormBrowserTests.cs:209-223`,
  `Nova.Browser.Tests\CampaignFormBrowserTests.cs:304+`.
- Hard-coded loops: `OpenDrawerAsync` (20 attempts) and `WaitForMutationSettlementAsync`
  (20 attempts) in `Nova.Browser.Tests\CampaignEvaluationBrowserTests.cs` (~lines 710, 757);
  `TabUntilFocusedAsync` (60 attempts) in `Nova.Browser.Tests\DashboardBrowserTests.cs:254`.
- Azurite surface: `Nova.Integration.Tests\Data\NovaAppHostFixture.cs` (waits only for `nova`
  healthy, then one-shot `CreateIfNotExistsAsync` at ~line 140) and
  `Nova.Integration.Tests\Http\IdentityHttpClientHelper.cs:28-54` (one-shot upload POST that throws
  `InvalidOperationException` on any non-204).
- Server side (context, not to be changed): `Nova\Features\Photos\ProfilePhotoService.cs` maps a
  `RequestFailedException` to `ServiceProblem.ServerError` (HTTP 500) and cleans up its own partial
  blobs; the upload is a safe retry target (entity row is upserted, each attempt uses a fresh blob
  batch prefix).

## Phase 1: Centralize hydration retry helpers into an env-tunable policy

Status: Complete

Suggested executor: orchestrator designs the policy; mechanical per-file call-site updates may be
delegated to a smaller-model sub-agent once the shared helpers exist.

- [x] Create `Nova.Browser.Tests\BrowserRetryPolicy.cs`: a static, lazily initialized policy that
      reads `NOVA_BROWSER_RETRY_MAX_ATTEMPTS` (default 60) and `NOVA_BROWSER_RETRY_DELAY_MS`
      (default 250) once; invalid/non-positive values fall back to the defaults. Expose
      `MaxAttempts` and `Delay`.
- [x] Create `Nova.Browser.Tests\InteractionHelpers.cs`: move the `ActUntilAsync` / `ClickUntilAsync`
      implementations there (preserve exact semantics: settle-check first, tolerate
      `PlaywrightException`/`TimeoutException`, 3s click timeout, descriptive `TimeoutException`
      on exhaustion) but drive attempt count and delay from `BrowserRetryPolicy`.
- [x] Delete the duplicated private copies and repoint call sites in `CampaignCloseoutBrowserTests.cs`,
      `TeamFormBrowserTests.cs`, `PlayerFormBrowserTests.cs`, and `CampaignFormBrowserTests.cs`.
      Verify `CampaignPlacementBrowserTests.cs`' `CheckUnresolvedOnlyAsync` path lands in the shared
      helper.
- [x] Convert the attempt/delay constants in `OpenDrawerAsync` and `WaitForMutationSettlementAsync`
      (`CampaignEvaluationBrowserTests.cs`) to the policy (keep their distinct per-interaction
      Playwright timeouts and break-on-visible structure).
- [x] Convert `TabUntilFocusedAsync` (`DashboardBrowserTests.cs`) to the policy for uniformity.

### Verification Plan

- `dotnet build Nova.slnx` — 0 errors.
- `dotnet format Nova.slnx --verify-no-changes` — clean for the changed files.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignEvaluationBrowserTests"` — green.
- Prove the env knobs execute end to end: run the same filter with
  `NOVA_BROWSER_RETRY_MAX_ATTEMPTS=80 NOVA_BROWSER_RETRY_DELAY_MS=500` set and expect green.

### Phase Summary

Centralized the four per-file copies of the SSR-hydration retry helpers into two shared types:
`BrowserRetryPolicy` (lazily initialized; reads `NOVA_BROWSER_RETRY_MAX_ATTEMPTS` / `NOVA_BROWSER_RETRY_DELAY_MS`
once with 60 / 250 defaults; invalid/non-positive values fall back) and `InteractionHelpers`
(`ActUntilAsync`, `ClickUntilAsync`, `TabUntilFocusedAsync`, all policy-driven). Deleted the duplicated
private copies from `CampaignCloseoutBrowserTests`, `TeamFormBrowserTests`, `PlayerFormBrowserTests`, and
`CampaignFormBrowserTests` and repointed their call sites to `InteractionHelpers.*`. Converted `OpenDrawerAsync`,
`WaitForMutationSettlementAsync` (`CampaignEvaluationBrowserTests`) and `CheckUnresolvedOnlyAsync`
(`CampaignPlacementBrowserTests`) to the policy while preserving their distinct per-interaction Playwright
timeouts and break-on-state structure. Added a symmetric, policy-driven `CloseDrawerAsync` helper and used it
at the five "open-then-immediately-Escape-close" hydration-proof sites after a flaky run surfaced a swallowed-Escape
race (see Phase 4); the keyboard-accessibility test's direct Escape-close was intentionally left unchanged.

## Phase 2: Azurite readiness wait + bounded seeding retry (shared infrastructure)

Status: Complete

- [x] `NovaAppHostFixture.InitializeAsync`: add an Azurite readiness poll. Best-effort wait for the
      `storage` resource to report healthy, then replace the one-shot `CreateIfNotExistsAsync` with
      a bounded retry (e.g. 20 attempts × 500 ms) over the container probe. On exhaustion, throw a
      descriptive exception that includes the last `RequestFailedException` so a genuinely broken
      Azurite fails fast and clearly rather than surfacing later as a per-test flake.
- [x] `IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync`: wrap the upload POST in
      a bounded retry (e.g. 5 attempts, ~500 ms delay) that:
      - retries only on `HttpRequestException` or 5xx responses (transient);
      - recreates the `MultipartFormDataContent` per attempt (each attempt's content is disposed);
      - rethrows immediately on non-transient statuses (existing `InvalidOperationException` with
        truncated body) and, if all attempts fail, includes each attempt's status in the message.
- [x] Keep the Azurite retry bounds as documented hard-coded constants in the shared helper —
      env-tunable windows are scoped to the browser hydration retries (Phase 1), per the issue
      wording. Note this decision in the XML doc comments.

### Verification Plan

- `dotnet build Nova.slnx` — 0 errors.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignLifecycleHttpTests"` — green (exercises the shared registration + upload helper).
- Code review of the retry predicate: only transient outcomes retried; multipart content never reused.

### Phase Summary

`NovaAppHostFixture.InitializeAsync` now best-effort waits (bounded 30 s) for the Azurite `storage` resource to
report healthy, then replaces the one-shot `CreateIfNotExistsAsync` with a bounded 20 × 500 ms container probe
that throws a descriptive `InvalidOperationException` carrying the last `RequestFailedException` as the inner
exception. `IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync` now wraps the upload POST in a
bounded 5 × 500 ms retry that retries only `HttpRequestException`/5xx, recreates `MultipartFormDataContent` per
attempt, rethrows immediately on non-transient statuses, and lists every attempt's status when all attempts fail.
Both bounds are hard-coded constants with XML-doc rationale; only the browser hydration retries are env-tunable.

## Phase 3: Update documentation

Status: Complete

- [x] `.github\skills\nova-testing\references\browser-suite.md`: document `BrowserRetryPolicy` /
      `InteractionHelpers` as the canonical hydration retry mechanism, the two
      `NOVA_BROWSER_RETRY_*` env vars with defaults, and the Azurite readiness/upload retry in the
      seeding path.
- [x] `.github\instructions\testing.instructions.md`: extend the browser-suite conventions section
      with the `NOVA_BROWSER_RETRY_*` vars and a pointer to the policy, and note the AppHost
      fixture's Azurite readiness poll.

### Verification Plan

- Manual review that both files reference the new helpers and env vars; no build required.

### Phase Summary

Documented `BrowserRetryPolicy` / `InteractionHelpers` and the two `NOVA_BROWSER_RETRY_*` vars (with their
defaults) in `.github/skills/nova-testing/references/browser-suite.md`, plus the Azurite readiness/upload retry
in the seeding path. Extended the browser-suite conventions section in
`.github/instructions/testing.instructions.md` with the env vars, a pointer to the policy, and the AppHost
fixture's Azurite readiness poll.

## Phase 4: Acceptance verification — 3 consecutive green 4-way parallel runs

Status: Complete

Suggested executor: long-running full-suite runs are good candidates for a `task`-type sub-agent
that reports pass/fail per run.

- [x] Run the full browser suite 3 consecutive times:
      `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` (assembly default
      `MaxThreads = 4` is the 4-way parallel configuration). Record each run's passed/skipped
      counts (expect 63 passed + 6 env-gated skips).
- [x] If any run fails: identify the failing class, adjust the policy defaults or per-interaction
      timeouts from Phases 1-2, and repeat until 3 consecutive runs are green.
- [x] Run the integration suite once (shared infrastructure changed):
      `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — expect green
      (356/356 baseline).
- [x] Run unit tests: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — green.
- [x] `dotnet format Nova.slnx --verify-no-changes` before commit.

### Verification Plan

- Three consecutive green full-suite browser runs recorded in the Phase Summary, plus one green
  integration-suite run and one green unit run.

### Phase Summary

- Full browser suite × 3 (default `MaxThreads = 4`): **63 passed + 6 env-gated skips each** (0 failures).
- Env-knob proof: `--filter-class "*CampaignEvaluationBrowserTests"` with
  `NOVA_BROWSER_RETRY_MAX_ATTEMPTS=80 NOVA_BROWSER_RETRY_DELAY_MS=500` — **18 passed + 1 skip** (green).
- Integration suite: **356/356 passed** (0 skipped, 0 failed).
- Unit suite: **1745/1745 passed** (0 skipped, 0 failed).
- `dotnet build Nova.slnx` — **0 warnings / 0 errors**; `dotnet format Nova.slnx --verify-no-changes` — clean.

The first two env-knob runs surfaced a pre-existing swallowed-Escape race in
`UrlState_SurvivesReload_AndBackForward_RestoresDrawer` (the drawer was still visible immediately after
`OpenParticipantAsync` + a single Escape). Hardened via the policy-driven `CloseDrawerAsync` helper (Phase 1),
which retries Escape until the drawer is hidden; the keyboard-accessibility test's direct Escape-close was left
unchanged because it waits for focus inside the drawer first.

## Final Recap

Harden the committed Playwright browser suite against 4-way parallel-run flakiness (issue #130) by
centralizing the duplicated SSR-hydration retry helpers into an environment-tunable policy and hardening the
shared seeding path against transient Azurite connection refusals.

- **Phase 1** created `BrowserRetryPolicy` (lazy, env-tunable, 60 × 250 ms defaults) and
  `InteractionHelpers` (`ActUntilAsync`/`ClickUntilAsync`/`TabUntilFocusedAsync`), deleted the four per-file
  private copies, repointed all call sites, converted the break-on-state loops (`OpenDrawerAsync`,
  `WaitForMutationSettlementAsync`, `CheckUnresolvedOnlyAsync`) to the policy, and added a policy-driven
  `CloseDrawerAsync` to close a demonstrated swallowed-Escape hydration race.
- **Phase 2** added an Azurite readiness poll plus a bounded 20 × 500 ms container-probe retry in
  `NovaAppHostFixture`, and a bounded 5 × 500 ms upload retry (transport/5xx only, fresh multipart per attempt)
  in `IdentityHttpClientHelper`; both bounds are hard-coded with XML-doc rationale.
- **Phase 3** updated the browser-suite reference doc and the testing instructions with the env vars, the new
  helpers, and the Azurite readiness/upload retry.
- **Phase 4** validated: 3 consecutive green full-suite runs (63 passed + 6 skips each), a green env-knob run,
  356/356 integration, 1745/1745 unit, 0-warning/0-error build, and a clean format check.

## Deployment Plan

1. Merge the PR against `main` (this is a test-infrastructure-only change; no product code, configuration, or
   runtime behavior changes).
2. No schema, migration, or deployment steps are required — the change affects only
   `Nova.Browser.Tests` and the test-only helpers in `Nova.Integration.Tests`.
3. Optional operator knobs (no action needed) for future CI/local tuning of the browser hydration retry window:
   `NOVA_BROWSER_RETRY_MAX_ATTEMPTS` (default 60) and `NOVA_BROWSER_RETRY_DELAY_MS` (default 250).
4. The Azurite/upload retry bounds are intentionally not environment-tunable; adjust the hard-coded constants
   in `NovaAppHostFixture` / `IdentityHttpClientHelper` only if the 20 × 500 ms / 5 × 500 ms windows prove
   insufficient on slower CI hosts.
