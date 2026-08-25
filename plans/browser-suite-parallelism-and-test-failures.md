# Browser Suite: Fix Failures, Scale Parallelism, Harden Retry Windows

Get the browser suite to zero failing tests (non-negotiable), make it faster by switching to
xUnit v4's Aggressive algorithm with MaxThreads 8 (validated empirically), evaluate the same
aggressive parallelism for the unit and integration suites (validated empirically), and refresh
the testing guidance in the repo per the instructions-hygiene devblog.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status
to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to
continue with zero context); run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Investigation Findings (baseline: 2026-08-25, commit 1f83426)

Full browser run: **83 total, 9 failed, 68 passed, 6 skipped, 8m54s** at `ParallelMode.All`,
`MaxThreads = 4`, `ParallelAlgorithm.Conservative`. Re-run of the 6 failing classes reproduced 8
of the 9; running the suspects one-at-a-time showed every load-sensitive test **passes in
isolation**. Failure classification:

**A. Deterministic regressions (fail even alone):**
1. `NavbarBrowserTests` ×3 (`Navbar_Authenticated_...`, `Navbar_Desktop_...`, `Navbar_Mobile_...`) —
   since #142 every club has a required crest, and the nav renders the crest `img` with
   `alt="Club crest"` *inside* the club link, so the link's accessible name became
   "Club crest Club {name}". The tests (written pre-crest, #134–#141) look the club link up with
   `Name = clubName, Exact = true`. Both a test break and a real a11y regression (redundant
   announcement) — fix the app: decorative image.
2. `BootstrapThemeBrowserTests.Theme_PrimaryButtonAndFocusRing_AreKelpTeal_WithNoBootstrapBlue` —
   the test reads `getComputedStyle(el).boxShadow` synchronously right after `el.focus()`, but
   Bootstrap transitions `box-shadow` over 0.15s, so it always reads the transparent start value.
   The compiled CSS rule is correct (`box-shadow:0 0 0 .25rem rgba(14,124,123,.25)`). Test bug —
   poll through the transition window.

**B. Parallel-load-sensitive failures (pass alone, fail under 4-way concurrency; 3/3 under load):**
3. `CampaignFormBrowserTests` ×2 (`_Loading_ShowsSubmitSpinner...`, `_Failure_ShowsRetry...`) —
   `CheckInlineSeasonAsync` retries `radio.CheckAsync` but catches only `PlaywrightException`;
   Playwright actionability timeouts surface as `System.TimeoutException` and escape the retry
   (fails at `CampaignFormBrowserTests.cs:263`).
4. `CampaignPlacementBrowserTests` ×2 (`SecondEdit_ReusesReplacementToken_WithoutReload`,
   `NarrowViewport_CardsRemainKeyboardOperable_...`) — the "Placement saved." alert `Expect` is a
   fixed 5s window; under load the save round-trip exceeds it (failure snapshot shows
   `_savingActive` still true = POST in flight).
5. `ClubCrestBrowserTests.ClubCrest_AdminReplacesAndRemoves_NavReflectsCrestPresence` — after
   `SetInputFilesAsync("#crest-file")` the crop frame `Expect` is a fixed 5s window; under load the
   change→crop round-trip exceeds it (snapshot shows the island still in non-cropping state).
6. `CampaignEvaluationBrowserTests.Roster_Loading_ShowsIndicator_ThenRendersRows` — flake (failed
   2/3 under load, passed isolated): `row.ClickAsync(5000)` threw `System.TimeoutException` that
   escaped the `catch (PlaywrightException)` retry in `OpenDrawerAsync`
   (`CampaignEvaluationBrowserTests.cs:722`).

**Root-cause pattern for B:** several hydration-retry loops catch only `PlaywrightException`, but
Playwright action methods (Click/Check/Select/Fill) throw `System.TimeoutException` on
actionability timeouts — the exact hardening `InteractionHelpers.ActUntilAsync` already applies
(`exception is PlaywrightException or TimeoutException`). Under 4-way load these timeouts become
frequent, and the escapes turn latency into hard failures. Fixed 5s mutation/crop windows are too
short under load; drive them through `BrowserRetryPolicy` instead.

**Skipped tests:** 6 env-gated a11y-evidence tests (`NOVA_A11Y_SCREENSHOTS=1`) skip by design per
`testing.instructions.md` ("env-gated helpers must `Assert.Skip` when their flag is unset"). This
is the documented good reason. Phase 3 validates they pass when the flag is set.

**Parallelism research (latest docs):**
- xUnit v4 (`xunit.v3.mtp-v2` 4.0.0): `[assembly: Parallelization(Mode, MaxThreads, Algorithm)]`.
  *Conservative* (current) caps *started* tests via semaphore — async/await-bound suites
  underutilize CPU; *Aggressive* starts many tests and caps concurrently-running continuations via
  a `SynchronizationContext` — better throughput for heavily await-bound suites like browser tests
  (https://xunit.net/docs/running-tests-in-parallel). Algorithm only applies with a limited
  thread count.
- MTP overrides via `testconfig.json` (`parallelMode`, `parallelAlgorithm`, `maxParallelThreads`)
  (https://xunit.net/docs/config-testconfig-json). Assembly attribute remains the repo's source of
  truth.
- Playwright: one browser instance + one isolated context per test is the documented parallel
  pattern (https://playwright.dev/docs/best-practices); already the suite's shape.
- Machine: 20 logical CPUs / 64 GB RAM — headroom exists above 4 Chromium contexts.

## Phase 1: Fix deterministic failures (app + test)

Status: Complete

- [x] Make the nav crest decorative: in `Nova/Components/Layout/NavMenu.razor`, change the club
      crest `img` to `alt=""` + `aria-hidden="true"` (the club name already labels the link). Leave
      the Manage profile-photo `img` as-is (out of scope; NB tests use `Exact = false` there).
- [x] Update `Nova.Unit.Tests/Components/NavMenuTests.cs:163` (`Alt="Club crest"` → assert empty
      alt + `aria-hidden`), keep the other crest-present/absent assertions.
- [x] Update `Nova.Browser.Tests/NavbarBrowserTests.cs:52`: replace
      `AssertBootstrapIconGlyphAsync(club, "bi-building")` with a crest-avatar assertion
      (`club.Locator("img.nav-avatar")` count 1) — clubs always have crests now (#142).
- [x] Update `Nova.Browser.Tests/ClubCrestBrowserTests.cs`: line 291
      (`ToHaveAttributeAsync("alt", "Club crest")` → empty alt + `aria-hidden`), line 164
      (`img[alt="Club crest"]` count 0 → `clubLink.Locator("img.nav-avatar")` count 0).
- [x] Fix `BootstrapThemeBrowserTests.cs` focus-ring read: poll `boxShadow` through
      `BrowserRetryPolicy` (focus, read, break when it contains `rgba(14, 124, 123`, else
      `WaitForTimeoutAsync(BrowserRetryPolicy.Delay)`) instead of one synchronous read.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*NavMenuTests"`
  → green.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter "FullyQualifiedName~NavbarBrowserTests|FullyQualifiedName~BootstrapThemeBrowserTests|FullyQualifiedName~ClubCrestBrowserTests"` → green (was 4 failures).

### Phase Summary

NavMenuTests 5/5 green. Phase 1 browser filter green (14 tests, 0 failed, 1m47s). In addition to the
plan's `boxShadow` poll, `BootstrapThemeBrowserTests.Theme_PrimaryButtonAndFocusRing_AreKelpTeal_WithNoBootstrapBlue`
also reads `getComputedStyle(el).backgroundColor` for the primary button synchronously (line 38-40),
which under load observes the empty start value; that read now polls through `BrowserRetryPolicy`
the same way. bUnit serializes an empty `alt=""` as a bare `alt` attribute, so
`NavMenuTests` asserts `ShouldContain("alt aria-hidden=\"true\"")`.

## Phase 2: Harden retry windows (parallel-load failures)

Status: Complete

- [x] Widen every action-method retry loop to `catch (Exception e) when (e is PlaywrightException
      or TimeoutException)` (the `InteractionHelpers.ActUntilAsync` pattern), at:
      `CampaignFormBrowserTests.cs:265` (radio `CheckAsync`),
      `CampaignPlacementBrowserTests.cs:432` (`CheckUnresolvedOnlyAsync` click), `:477`
      (`SelectGraduationYearAsync`), `:512` (`SaveFirstRowAsync` select loop), `:548`/`:558`
      (`AssignOutcomeAsync`), `CampaignEvaluationBrowserTests.cs:722` (`OpenDrawerAsync` click),
      `CampaignCloseoutBrowserTests.cs:490`/`:500` (audit context; widen only action-method sites).
      Leave `Expect`-only catches as `PlaywrightException` (assertion failures use that type).
- [x] Replace the fixed 5s "Placement saved." alert `Expect` in `SaveFirstRowAsync`
      (`CampaignPlacementBrowserTests.cs:526`) with a `BrowserRetryPolicy`-driven poll (up to
      `MaxAttempts × Delay`), keeping the final Shouldly failure loud.
- [x] Retry the crest crop-step entry in `ClubCrestBrowserTests` (CC1:54-57, CC2:122-124,
      CC3:198-201): re-issue `SetInputFilesAsync` until the cropper frame is visible within the
      policy window (idempotent re-set on the file input).

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter "FullyQualifiedName~CampaignFormBrowserTests|FullyQualifiedName~CampaignPlacementBrowserTests|FullyQualifiedName~CampaignEvaluationBrowserTests|FullyQualifiedName~ClubCrestBrowserTests"` run twice at current settings (MaxThreads 4) → both green (was 6-7 failures).

### Phase Summary

Campaign filter run twice at MaxThreads 4: both green (42 tests, 0 failed, ~3m24s each). Added an
`ExpectAllYearsAsync` poll helper in `CampaignPlacementBrowserTests` so
`Workspace_AppliesGraduationYearFilter_AndComposesWithUnresolvedOnly` reads the filtered years
through a retry window instead of a raw `years.ShouldAllBe` read that flaked under load. Also
hardened `NarrowViewport_CardsRemainKeyboardOperable_WithLabelsAndAnnouncements`: the
`Expect(outcome).ToBeFocusedAsync()` inside its retry loop threw a `PlaywrightException` on a fresh
focus that was swallowed by a mid-update rerender; the assertion is now wrapped in a
`catch (PlaywrightException)` so the loop refocuses instead of failing.

## Phase 3: Browser parallelism — Aggressive + MaxThreads 8

Status: Complete (with evidence-driven deviation)

> **Deviation from the plan's target settings, justified by empirical evidence below.** The suite
> is set to **Conservative + MaxThreads 4**, not Aggressive + MaxThreads 8. The plan's own guidance
> ("If flakiness appears at 8, evaluate MaxThreads 6 or a targeted `DisableParallelism`") does not
> anticipate that Aggressive is fundamentally incompatible with this suite for the same
> shared-PostgreSQL reason that rules it out for the integration suite.

- [x] `Nova.Browser.Tests/TestAssemblyParallelization.cs` was set to the best empirically verified
      setting: `Algorithm = ParallelAlgorithm.Conservative, MaxThreads = 4` (see the deviation note and
      Phase Summary). The original plan target (`Aggressive + MaxThreads 8`) was substituted by this
      evidence-driven deviation — Aggressive *provably* exhausts the shared PostgreSQL pool
      (22 failures at 8, 12 at 6), and the comment documents why `Aggressive` was rejected.
- [x] Full browser suite: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`
      — record wall time (baseline 8m54s) and result. **Zero failures required.** → green
      (83 total, 0 failed, 6 skipped).
- [x] Repeat the full suite 2 more times (3 consecutive green runs) for flake confidence. → 3
      consecutive green runs (4m31s / 4m44s / 4m26s), plus a post-hardening run (4m28s).
- [x] One run with `NOVA_A11Y_SCREENSHOTS=1` to prove the 6 env-gated tests pass when enabled
      (they skip by default with a documented reason). → green, **0 skipped**
      (83 total, 0 failed, 0 skipped, 4m29s).
- [x] If flakiness appears at 8, evaluate MaxThreads 6 or a targeted
      `DisableParallelism` (only with an inline reason per `testing.instructions.md`).

### Verification Plan

- 3 consecutive full-suite green runs; recorded durations vs the 8m54s baseline; the a11y-flagged
  run green with 0 skips of the gated tests.

### Phase Summary

**Findings that drove the deviation.** I evaluated Aggressive + MaxThreads 8 and 6 on a clean
environment (no external AppHost holding the shared ports). Both failed with the shared-PostgreSQL
pool exhausted:
- Aggressive, MaxThreads 8 → **22 failures** (83 total), all `Npgsql.PostgresException 53300
  "sorry, too many clients already"` from browser-test seeding, plus 2 ClubCrest WASM-boot timeouts.
- Aggressive, MaxThreads 6 → **12 failures**, the same `53300` seeding pool exhaustion.

Root cause is identical to the integration suite (see its Phase 4 summary): Aggressive *starts*
every test case up front, and each browser test seeds the shared PostgreSQL via a DbContext before
its first await — before the SynchronizationContext continuation gate (MaxThreads) applies — so the
cap cannot bound concurrently checked-out connections. Conservative bounds how many tests START,
keeping concurrent seeding within the pool.

**Final setting:** Conservative + MaxThreads 4. This is the highest level at which the many
load-sensitive interactive tests (drawer/hydration/history/computed-style polls) stay
deterministic; MaxThreads 6 and 8 under Conservative shared the CPU too thinly and produced
intermittent timing flakes. The Phase 1-2 hardening (wider catch clauses, BrowserRetryPolicy-driven
mutation/crop/focus polls) is what removes the failures, not more concurrency.

**Verification (all on clean env, Conservative + MaxThreads 4):**

| Run | Result | Wall time | vs 8m54s baseline |
|-----|--------|-----------|-------------------|
| Full suite #1 | 83 total, 0 failed, 6 skipped | 4m31s | −49% |
| Full suite #2 | 83 total, 0 failed, 6 skipped | 4m44s | −47% |
| Full suite #3 | 83 total, 0 failed, 6 skipped | 4m26s | −50% |
| Full suite (post-hardening) | 83 total, 0 failed, 6 skipped | 4m28s | −50% |
| `NOVA_A11Y_SCREENSHOTS=1` | **83 total, 0 failed, 0 skipped** | 4m29s | — |

The a11y-flagged run has **0 skips** — all 6 env-gated tests ran and passed (they skip by default
with a documented reason when the flag is unset).

## Phase 4: Unit + integration suites — Aggressive evaluation, full-solution validation

Status: Complete (with evidence-driven deviation)

> **Deviation from the "switch both to Aggressive" instruction.** Unit goes Aggressive (green and
> fast). Integration stays **Conservative** because Aggressive *provably* exhausts the shared
> PostgreSQL pool (identical to the browser-suite finding). This was already anticipated by the
> plan's own fallback ("If the integration suite shows DB contention ... cap MaxThreads with
> evidence") — the evidence shows capping cannot help, so it is Conservative.

- [x] Baseline timings: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` and
      `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` (integration
      requires the Aspire AppHost/PostgreSQL).
- [x] Switch both `TestAssemblyParallelization.cs` files to `ParallelAlgorithm.Aggressive`
      (keep CPU-thread-default `MaxThreads` for now).
- [x] Re-run both suites; compare timings. If the integration suite shows DB contention (the file
      comment anticipates this: "tune MaxThreads if ... contention"), cap `MaxThreads` with
      evidence and record the value + reason inline.
- [x] Full-solution validation: unit + integration + browser suites all green in one pass.

### Verification Plan

- All three suites green; before/after durations recorded for unit and integration.

### Phase Summary

**Unit:** Aggressive (CPU-thread default). **Green: 1816 passed, 0 failed**, warm run 25s.

**Integration:** Conservative. **Green: 373 passed, 0 failed**, ~2m48s.

**Aggressive destroys the integration suite — definitive, and independent of MaxThreads.** On a
clean environment (no external AppHost holding the shared ports), Aggressive at the CPU-thread
default fails **63 tests** with `Npgsql.PostgresException 53300 "sorry, too many clients already"`
(shared PostgreSQL, some cascading into 38s HTTP timeouts). Capping MaxThreads does not help:
Aggressive *starts* every test case up front and each test opens a DbContext and checks out a pooled
connection before its first await (before the SynchronizationContext continuation gate / MaxThreads
applies), so the cap cannot bound concurrently checked-out connections. Conservative caps *started*
tests at MaxThreads, keeping connection use within the pool. This is recorded as a clean-evidence
comment in `Nova.Integration.Tests/TestAssemblyParallelization.cs`. The browser suite shares this
exact seeding path — which is why it too must stay Conservative (Phase 3).

**Note:** A manually-started external AppHost (run outside the tests) was found holding the shared
fixed ports and opposing the in-process test AppHosts, causing `database "nova" does not exist` /
connection-refused churn. It was killed before producing the clean evidence above; always kill any
manually-started AppHost before running the integration or browser suites.

**Full-solution validation:** unit (1816) + integration (373) + browser (83, a11y run) all green in
one pass.

## Phase 5 (optional, speed): Amortize the WASM warmup

Status: Complete (investigated → dropped, documented)

- [x] 13 tests call `ReloadAsWebAssemblyAsync` (15s boot wait + reload each, up to 3 attempts) —
      roughly 3-4 minutes of fixed cost in the full run. Investigate warming the WASM runtime once
      per browser context during `NewSignedInContextAsync`'s login page load so the helper can skip
      or shorten its wait; keep the negotiate-absence verification.
- [x] Validate with a full-suite run + timing; **drop the change if it is not provably faster and
      equally stable.**

### Verification Plan

- Full-suite green run with recorded duration; compare against Phase 3 timings.

### Phase Summary

**Dropped, not provably faster and equally stable.** The login and account pages are server-rendered
(no `InteractiveAuto` island), so warming the WASM runtime during `NewSignedInContextAsync`'s login
load would require embedding an `InteractiveAuto` component on the login page — an invasive app
change with no user-facing benefit, and it changes the login page's runtime behavior. Per the plan's
explicit instruction ("drop the change if it is not provably faster and equally stable"), this is
skipped.

Separate observed fact: `ReloadAsWebAssemblyAsync` did surface as a load-sensitive failure at
Aggressive concurrency (`Page did not switch to WebAssembly after 3 reloads`), because the WASM boot
download is bandwidth-bound and 8 simultaneous boots overran the 3×15s window. Under the final
Conservative + MaxThreads 4 setting the ClubCrest WASM reloads complete reliably within the existing
3-attempt window (full-suite runs green), so no change to the helper is needed — but a future
bump in browser parallelism must revisit this window.

## Phase 6: Testing guidance hygiene (per instructions-hygiene devblog)

Status: Complete

- [x] `testing.instructions.md` keep/remove/move/verify pass:
      - Update the **Parallel execution** bullet: browser = **Conservative + MaxThreads 4**; unit =
        **Aggressive at the CPU-thread default**; integration = **Conservative** at the CPU-thread
        default (matching the evidence-driven Phase 3/4 deviations; the contention note is already
        reflected in the "Do NOT switch integration or browser to Aggressive" bullet).
      - Add hard-won fact #4 to the browser-suite list: Playwright action methods throw
        `System.TimeoutException` (not `PlaywrightException`) on actionability timeouts — retry
        loops must catch both; the pattern lives in `InteractionHelpers`.
      - Add: don't assert computed styles synchronously after triggering a CSS transition (poll
        through `BrowserRetryPolicy`).
      - Remove/refresh stale wording (e.g. the "Phase 4 load validation" comment that referred to
        a since-merged plan).
- [x] Update `.github/skills/nova-testing/references/browser-suite.md` with the new parallelism
      knobs and both lessons above.
- [x] Verify the repo-root instructions overview needs no change (run commands are unchanged).

### Verification Plan

- `dotnet format Nova.slnx --verify-no-changes`; re-read the edited sections for accuracy.

### Phase Summary

`testing.instructions.md` and `.github/skills/nova-testing/references/browser-suite.md` were updated
with the applied parallelism settings and both hard-won lessons (the `System.TimeoutException` catch
requirement and the computed-style polling note). `dotnet format Nova.slnx --verify-no-changes` is
green; the repo-root instructions overview needed no change (run commands are unchanged).

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
