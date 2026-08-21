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
  approved evaluator over HTTP, seeds the workspace (60 participants = 2 pages, active + archived
  tags, an archived pre-application), returns `SeededEvaluationWorkspace`.
- `Nova.Browser.Tests\CampaignEvaluationBrowserTests.cs` — the scenario tests plus the
  `OpenWorkspaceAsync`/`OpenParticipantAsync` helpers (which now delegate to
  `InteractionHelpers`/`BrowserRetryPolicy` for hydration retries).
- `Nova.Browser.Tests\InteractionHelpers.cs` — shared SSR-hydration interaction retry helpers
  (`ActUntilAsync`, `ClickUntilAsync`, `TabUntilFocusedAsync`), all driven by `BrowserRetryPolicy`.
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

## The three Blazor interaction pitfalls

These were each discovered by a failing browser run; they are not inferable from the code. Follow
them when writing any browser test:

1. **Assertions entry point.** Playwright .NET has no bare `Expect(...)`. The static class is
   `Microsoft.Playwright.Assertions` — add `using static Microsoft.Playwright.Assertions;` (or a
   `<Using Include="Microsoft.Playwright.Assertions" Static="true" />` in the csproj) and call
   `await Expect(locator).ToBeVisibleAsync();`.
2. **Client-side navigation never fires a document load.** Blazor
   `NavigationManager.NavigateTo` rewrites history without a `load` event, so
   `WaitForURLAsync`/`GoBackAsync`/`GoForwardAsync` with the default `WaitUntil` hang until
   timeout. Always pass `WaitUntilState.Commit` for Blazor-driven URL changes, and wait for
   state (e.g. `ToHaveTextAsync("51 of 60")`) before asserting on `page.Url`.
3. **SSR prerender swallows early clicks.** Roster rows are prerendered; clicks before the
   interactive circuit attaches are ignored, and after the drawer opens the backdrop intercepts
   re-clicks. Use a retry helper that clicks until the drawer is actually visible and never
   re-clicks once it is (`OpenParticipantAsync`). For URL-state tests, open and close a
   participant first — a successful drawer open proves hydration before filters are driven.

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
`WaitForMutationSettlementAsync`, and `CheckUnresolvedOnlyAsync`), so those windows grow with the
knobs while keeping their distinct per-interaction timeouts and break-on-state structure.

## Fixture and bootstrap

- Mark the test class `[Collection(BrowserSuiteCollection.Name)]` and inject
  `BrowserSuiteFixture` via primary constructor.
- `await fixture.NewSignedInContextAsync(email, password, viewport?)` returns a fresh
  `IBrowserContext` (own cookie jar, `IgnoreHTTPSErrors = true` for the untrusted dev cert)
  already signed in through the real `/Account/Login` page. `viewport` defaults to 1280×800;
  pass `new ViewportSize { Width = 480, Height = 800 }` for narrow-layout scenarios.
- `fixture.CloseCampaignAsAdminAsync(campaignId, adminUserId, clubId, ct)` drives
  `CampaignLifecycleService` directly (there is no close UI or endpoint yet) through the
  fixture's `CreateTenantContextFactory()` under a `UseUser` scope that restores the previous
  simulated user on completion. Use it for stale-close/conflict scenarios.
- Dispose each context with `await using`.

## Seeding

- Call `EvaluationSeed.SeedAsync(fixture.AppHost, ct)` per test (fresh unique data each time;
  the database is shared across the collection). It registers users over HTTP (never through UI
  registration), sets distinct display names so actor-metadata assertions are meaningful, and
  seeds data through the admin EF context.
- Add new shared seeding primitives to `SeedingHelpers` — do not copy them per file.
- The shared AppHost fixture now best-effort waits for the Azurite `storage` resource to report
  healthy before probing the `profile-photos` container, and `IdentityHttpClientHelper` retries the
  profile-photo upload POST on transient failures (transport errors / 5xx) with a fresh multipart
  payload per attempt. Those Azurite/upload retry bounds are hard-coded; only the browser hydration
  retries are environment-tunable.
- The login helper fills `GetByLabel("Email")`/`GetByLabel("Password")` and clicks the
  **exact** "Log in" button (`GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true })`
  — a substring match also hits "Log in with a passkey").

## Conventions

- Env-gated helpers must `Assert.Skip(...)` when their flag is unset — a green run must mean the
  assertions executed. Flags: `NOVA_BROWSER_HEADED=1` (visible browser),
  `NOVA_A11Y_SCREENSHOTS=1` (accessibility evidence: screenshots plus computed contrast and
  touch-target measurements written to `%TEMP%\nova-a11y-screenshots`).
- Accessibility regression assertions stay in the scenario that exercises the control: touch
  targets ≥24×24 CSS px on drawer controls, tag chip text contrast ≥4.5:1 (WCAG AA) against its
  club-defined background.
- Use Playwright's auto-retrying assertions (`ToBeFocusedAsync`, `ToBeVisibleAsync`,
  `ToHaveTextAsync`) instead of one-shot `EvaluateAsync` probes; `document.activeElement`
  is assigned after render, so a single probe races.
- Polling helpers must fail loudly: when a mutation may settle as success *or* conflict, poll for
  either, then throw a descriptive `TimeoutException` if neither appeared.
- One behavior per test; `Subject_Outcome_Condition` naming, Shouldly assertions, and
  `TestContext.Current.CancellationToken` — same conventions as the other test projects.

## Writing pattern

1. `[Collection(BrowserSuiteCollection.Name)]` + primary-constructor `BrowserSuiteFixture`.
2. `var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, ct);`
3. `await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);`
4. `await OpenWorkspaceAsync(page, seed.CampaignId);` then interact through the helper
   (`OpenParticipantAsync(page, page.Locator($"#roster-row-{id}"))`).
5. Assert visible outcomes with `Assertions.Expect(...)` and Shouldly for computed values.

## Run commands

```powershell
# one-time per machine:
Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium
# set PLAYWRIGHT_BROWSERS_PATH to relocate the browser cache

dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignEvaluationBrowserTests"
```

Local-only: CI runs build and unit tests only, so run the suite locally before merge.
