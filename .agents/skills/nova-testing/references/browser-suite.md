# Browser suite: Playwright against the Aspire AppHost

`Nova.Browser.Tests` is the committed, local-only browser coverage for real UI flows. It boots the
Aspire AppHost (same as the integration tests), launches Playwright Chromium, and drives the real
pages with real Identity logins. Write a browser test when bUnit cannot prove the behavior
(interactive attach, focus/keyboard, history/URL state, multi-user sessions, real HTTP
mutations); use bUnit for everything that fits there.

Canonical files:

- `Nova.Browser.Tests\BrowserSuiteFixture.cs` — `BrowserSuiteFixture` (starts `NovaAppHostFixture`
  + one shared Chromium, exposes `NewSignedInContextAsync` and `CloseCampaignAsAdminAsync`) and
  the `BrowserSuiteCollection` collection fixture.
- `Nova.Browser.Tests\EvaluationSeed.cs` — `EvaluationSeed.SeedAsync`: registers an admin and an
  approved evaluator over HTTP, seeds the workspace (60 participants, active + archived
  tags, an archived pre-application), returns `SeededEvaluationWorkspace`.
- `Nova.Browser.Tests\CampaignEvaluationBrowserTests.cs` — the scenario tests plus the
  `OpenWorkspaceAsync`/`OpenParticipantAsync`/`CloseDrawerAsync` helpers (which delegate to
  `InteractionHelpers`/`BrowserRetryPolicy` for hydration retries).
- `Nova.Browser.Tests\InteractionHelpers.cs` — shared SSR-hydration retries
  (`ActUntilAsync`, `ClickUntilAsync`, `TabUntilFocusedAsync`) driven by `BrowserRetryPolicy`, plus
  `NavigateEnhancedAsync` for document completion and `WithNavigationDiagnosticsAsync` for evidence.
- `Nova.Browser.Tests\BrowserRetryPolicy.cs` — lazily-initialized, environment-tunable retry policy
  (`NOVA_BROWSER_RETRY_MAX_ATTEMPTS` / `NOVA_BROWSER_RETRY_DELAY_MS`; defaults 60 × 250 ms).
- `Nova.Integration.Tests\Http\SeedingHelpers.cs` — shared seeding primitives
  (`UniqueEmail`, `CreateClubAsync`, `RefreshClubMembershipCookieAsync`, `UpdateUserAsync`,
  `SeedCampaignWithParticipantsAsync`, `InsertTagDefinitionAsync`). Internal, shared with the
  browser project via `InternalsVisibleTo("Nova.Browser.Tests")` in `Nova.Integration.Tests`.
- `Nova.Integration.Tests\Data\NovaAppHostFixture.cs` — `NovaBaseUri` and
  `CreateTenantContextFactory()` were added for the browser suite; `CreateNovaHttpClient`
  reuses `NovaBaseUri`. It also best-effort waits for Azurite `storage` readiness and retries the
  `profile-photos` container probe (see "Seeding" below).

## Blazor interaction pitfalls

Use these patterns when the scenario crosses prerender, interactive attachment, or navigation:

1. **Assertions entry point.** Playwright .NET has no bare `Expect(...)`. The static class is
   `Microsoft.Playwright.Assertions` — add `using static Microsoft.Playwright.Assertions;` (or a
   `<Using Include="Microsoft.Playwright.Assertions" Static="true" />` in the csproj) and call
   `await Expect(locator).ToBeVisibleAsync();`.
2. **URL changes, document completion and attachment are separate.** Same-document Blazor
   navigation does not fire `load`; use `WaitUntilState.Commit` for its URL/history waits.
   Nova's per-page enhanced navigation can change the URL and render interactive state before
   the fetched destination HTML is applied. Wrap an enhanced link/history action in
   `InteractionHelpers.NavigateEnhancedAsync`, which observes navigation start/end, then assert
   the exact destination state and URL. It does not prove attachment and is not for full loads,
   reloads, new tabs or query-only changes that do not fetch a document. See
   `PlayersDirectoryBrowserTests.EnhancedFormResponsePreservesTheMountedFormAndItsDraftAsync`.
3. **Prove attachment through a safe handler.** When an interaction or assertion depends on Blazor
   handlers, first use a reversible non-submitting handler, such as opening/closing the participant
   drawer or Cancel on the player form. Button probes must use `type="button"`. Native GET-form
   tests with JavaScript disabled do not need attachment. A native link works before attachment;
   submitting a blank form can issue a native POST, so neither is an attachment probe.
   Use `ClickUntilAsync` only while the
   handler's outcome is absent, and close any resulting dialog. If the probe navigates, also await
   enhanced document completion before reopening the form. See the ordinary-member scenario in
   `PlayersDirectoryBrowserTests` and `OpenParticipantAsync` for drawer attachment.
4. **Playwright action methods throw `System.TimeoutException`, not `PlaywrightException`.** On
   actionability timeouts (`Click`/`Check`/`Select`/`Fill`/`Focus`), Playwright .NET surfaces
   `System.TimeoutException`, so a hydration-retry loop that catches only `PlaywrightException`
   lets the escape turn latency into a hard failure. Catch both — `catch (Exception e) when (e is
   PlaywrightException or TimeoutException)` — the `InteractionHelpers.ActUntilAsync` pattern.
   Keep `Expect`-only catches as `PlaywrightException` (assertion failures use that type).
5. **Never read computed styles synchronously after triggering a CSS transition.** Bootstrap
   transitions (e.g. `box-shadow` ~0.15s) and an async-loading stylesheet mean a single
   `getComputedStyle().backgroundColor`/`.boxShadow` read right after `focus()` observes the
   transparent/empty start value under load. Poll through `BrowserRetryPolicy` (read, break when
   it matches, else `WaitForTimeoutAsync(BrowserRetryPolicy.Delay)`).
6. **Attachment can replace a visible or focused element.** For bounded keyboard retries, use
   `locator.PressAsync("Enter")` on each attempt so Playwright resolves and focuses the current
   control. A one-time `FocusAsync` followed by document-level key presses can target the body
   after replacement. Prove attachment with a successful interaction before reading a bounding box
   or testing preservation of user focus. For navigation-focus regressions, exercise the real
   destination through attachment, including delayed content and navigation between routes that
   reuse a shell; immediate SSR focus alone is insufficient. See `ClubOverviewBrowserTests`.
7. **HTTP interception needs a verified interactive document.** Pass a caller-specific UI action
   to `WasmWarmupHelper.ReloadAsWebAssemblyAsync` when a scenario depends on browser HTTP calls.
   The helper observes server negotiation through that action; no negotiation during a fixed delay
   alone is not attachment proof. Keep the verified document, and observe the intercepted request
   before asserting an injected error. Photo islands may have no startup API read, so a generic
   “any API request” probe is not a substitute for their actual upload/crop interaction.
8. **A retry loop cannot drive a debounced control.** `CampaignRosterFilters` raises its search from
   `@oninput`, and the destinations debounce it (`SearchDebounceMilliseconds`, 350 ms).
   `ActUntilAsync` re-runs the act on every attempt with a 250 ms default delay, so each retry is a
   fresh input event that cancels the pending debounce and restarts it — the timer never fires and
   the search never applies. A loop that retries the typing therefore looks exactly like a broken
   search field. Drive a debounced control with one act, after proving attachment with a different
   interaction, and settle on the **final** value (`WaitForURLAsync(url =>
   url.Contains("search=Player%2001"))`): settling on “any search applied” returns during the first
   keystroke pause and then races the later navigation.
9. **A settle predicate must not wait.** Playwright state queries such as `IsEnabledAsync()` wait for
   a matching element and then throw, so a predicate like `() => page.Locator("#place-outcome")
   .IsEnabledAsync()` aborts the retry loop with a 30 s timeout before the first click lands, instead
   of reporting “not yet”. Guard every waiting state query with a non-waiting existence check first:
   `await locator.CountAsync() > 0 && await locator.IsEnabledAsync()`. `IsVisibleAsync()` is the
   exception — it does not wait.

## Parallelization

The browser suite runs `ParallelMode.All`, `ParallelAlgorithm.Conservative`, `MaxThreads = 4`
(`Nova.Browser.Tests\TestAssemblyParallelization.cs`). Do **not** switch it to Aggressive: even at
MaxThreads 8 and 6, Aggressive failed 22 / 12 tests with `Npgsql.PostgresException 53300 "sorry,
too many clients already"` because it *starts* every test case up front and each browser test seeds
the shared PostgreSQL via a DbContext before its first await — before the SynchronizationContext
continuation gate (MaxThreads) applies, so the cap cannot bound concurrently checked-out
connections. Conservative bounds how many tests START. MaxThreads 4 (not 6 or 8) also keeps the
many load-sensitive interactive tests deterministic; raising it above 4 shared the CPU too thinly
and produced intermittent timing flakes. The hardening in this file (wider catch clauses,
`BrowserRetryPolicy`-driven mutation/crop/focus polls) is what removes the failures, not more
concurrency.

## Hydration retry policy

The four per-file copies of the SSR-hydration click/act/tab retry helpers were collapsed into two
shared types in `Nova.Browser.Tests`:

- `BrowserRetryPolicy` — a static, lazily-initialized policy. It reads the environment exactly once
  and exposes `MaxAttempts` (default 60) and `Delay` (default 250 ms). Missing, invalid, or
  non-positive values fall back to the defaults.
- `InteractionHelpers` — `ActUntilAsync`/`ClickUntilAsync`/`TabUntilFocusedAsync`, all driven by
  `BrowserRetryPolicy`. Per-interaction Playwright timeouts (the 3 s click timeout and the 400 ms
  focus probe) stay hard-coded; only the attempt count and the between-attempt delay are tunable.

Environment knobs:

- `NOVA_BROWSER_RETRY_MAX_ATTEMPTS` — attempt budget for hydration retries (default `60`).
- `NOVA_BROWSER_RETRY_DELAY_MS` — delay in milliseconds between attempts (default `250`).

The same policy also drives the break-on-visible/break-on-URL loops (`OpenDrawerAsync`,
`CloseDrawerAsync`, and `WaitForMutationSettlementAsync`), so those
windows grow with the knobs while keeping their distinct per-interaction timeouts and break-on-state
structure. Use `CloseDrawerAsync` (Escape-until-hidden) rather than a single `Escape` + `ToBeHiddenAsync`
when closing the participant drawer after opening it as a hydration proof.

## Fixture and bootstrap

- Mark the test class `[Collection(BrowserSuiteCollection.Name)]` and inject
  `BrowserSuiteFixture` via primary constructor.
- `await fixture.NewSignedInContextAsync(email, password, viewport?)` returns a fresh
  `IBrowserContext` (own cookie jar, `IgnoreHTTPSErrors = true` for the untrusted dev cert)
  already signed in through the real `/Account/Login` page. `viewport` defaults to 1280×800;
  pass `new ViewportSize { Width = 480, Height = 800 }` for narrow-layout scenarios.
- `fixture.CloseCampaignAsAdminAsync(campaignId, adminUserId, clubId, ct)` drives
  `CampaignLifecycleService` directly through the
  fixture's `CreateTenantContextFactory()` under a `UseUser` scope that restores the previous
  simulated user on completion. Use it for stale-close/conflict scenarios.
- Dispose each context with `await using`.

## Seeding

- Call `EvaluationSeed.SeedAsync(fixture.AppHost, ct)` per test (fresh unique data each time;
  the database is shared across the collection). It registers users over HTTP (never through UI
  registration), sets distinct display names so actor-metadata assertions are meaningful, and
  seeds data through the admin EF context.
- Add new shared seeding primitives to `SeedingHelpers` — do not copy them per file.
- For current-season, supersession, and Closed-record scenarios, follow the shared
  [campaign lifecycle seed requirements](unit-sqlite-harness.md#campaign-lifecycle-seeds).
- The shared AppHost fixture best-effort waits for the Azurite `storage` resource to report healthy
  before probing the `profile-photos` container, and `IdentityHttpClientHelper` retries the
  profile-photo upload POST on transient failures (transport errors / 5xx) with a fresh multipart
  payload per attempt. Those Azurite/upload retry bounds are hard-coded; only the browser hydration
  retries are environment-tunable. This is deliberate (issue #130): the env knobs are scoped to
  hydration retries, and the seeding bounds stay fixed so the shared fixture is deterministic — tune
  them by editing the constants in `NovaAppHostFixture` / `IdentityHttpClientHelper`, not via env vars.
- The login helper fills `GetByLabel("Email")`/`GetByLabel("Password")` and clicks the
  **exact** "Log in" button (`GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true })`
  — a substring match also hits "Log in with a passkey").

## Conventions

- Env-gated helpers must `Assert.Skip(...)` when their flag is unset — a green run must mean the
  assertions executed. Flags: `NOVA_BROWSER_HEADED=1` (visible browser),
  `NOVA_A11Y_SCREENSHOTS=1` (accessibility evidence: screenshots plus computed contrast and
  touch-target measurements written to `%TEMP%\nova-a11y-screenshots`).
- Accessibility regression assertions stay in the scenario that exercises the control. Follow
  Nova's 44px phone target contract; older 24px baseline assertions do not define new designs.
  Tag chip text needs contrast ≥4.5:1 (WCAG AA) against its club-defined background.
- Use Playwright's auto-retrying assertions (`ToBeFocusedAsync`, `ToBeVisibleAsync`,
  `ToHaveTextAsync`) instead of one-shot `EvaluateAsync` probes; `document.activeElement`
  is assigned after render, so a single probe races.
- Polling helpers must fail loudly: when a mutation may settle as success *or* conflict, poll for
  either, then throw a descriptive `TimeoutException` if neither appeared.
- For normalized workspace return URLs, compare origin/path/fragment plus the exact query key set
  and every value, allowing canonical key ordering. A raw URL-string comparison tests serialization
  order as well as retained state; require it only when that order is contractual. See
  `AssertEquivalentWorkspaceUrl` in `CampaignPlaceCorrectionBrowserTests.cs`.
- One behavior per test; `SubjectOutcomeConditionAsync` naming for async tests, Shouldly assertions, and
  `TestContext.Current.CancellationToken` — same conventions as the other test projects.

## Writing pattern

1. `[Collection(BrowserSuiteCollection.Name)]` + primary-constructor `BrowserSuiteFixture`.
2. `var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, ct);`
3. `await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);`
4. `await OpenWorkspaceAsync(page, seed.CampaignId);` then interact through the helper
   (`OpenParticipantAsync(page, page.Locator($"#roster-row-{id}"))`).
5. Assert visible outcomes with `Assertions.Expect(...)` and Shouldly for computed values.

## Run commands

Build first and serialize Aspire-backed suites as described in `AGENTS.md`.

```powershell
# one-time per machine:
Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium
# set PLAYWRIGHT_BROWSERS_PATH to relocate the browser cache

dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class "*CampaignEvaluationBrowserTests"

# diagnose a stalled run without changing retry budgets:
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --output Detailed --long-running 90 --xunit-diagnostics on
```

`--filter-class` matches the class-name **suffix**, so the wildcard belongs at the front and a second class
repeats the option. A pattern with a wildcard in the middle (`*Player*BrowserTests`) matches nothing and the
run still reports success, so read `total` before treating a selection as evidence.

For required run scope and evidence reuse, follow the
[PR test gate](../../../../AGENTS.md#pull-request-test-gate).

## Diagnosing a stalled or slow run

Identify the active case from the diagnostic command above and inspect its awaited operation. An
outer retry count is not an elapsed time limit: each Playwright action may spend its own timeout on
every attempt. Check rendered select values and locators before increasing retry budgets. Tests
holding intercepted requests must release them and remove routes in `finally` so a failed assertion
cannot strand teardown.

Distinguish a functional completion deadline from a performance requirement. A scale scenario should
fail on a surfaced read error, assert exact counts/page contents, and record elapsed time; it proves
a latency target only when that target was explicitly specified. Timeout changes remain quality-control
changes subject to the rationale and review rule in `AGENTS.md`, not a default cure for a slow run.
For a load-sensitive failure, retain the failing URL/rendered state and rerun the unchanged case
without concurrent build/format work before interpreting timing. An isolated pass does not prove
contention caused the failure or replace the required full-suite run.

Wrap enhanced-navigation regressions in `InteractionHelpers.WithNavigationDiagnosticsAsync` so failures in
later typing or assertions retain the URL, navigation events, history entries and rendered state,
not just failures inside the navigation wait. Capture is best-effort and preserves the original
exception; it neither retries the scenario nor extends its timeout.

For modified-click navigation, observe new pages through the browser context; an opener-specific
popup event is not guaranteed for native links. The page event can precede the initial document:
await that page's `WaitForLoadStateAsync(LoadState.DOMContentLoaded)` before destination assertions.
Retain assertions for the destination and unchanged source-page draft.
