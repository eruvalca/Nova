---
applyTo: "Nova.Browser.Tests/**"
description: "Nova browser-suite hydration, navigation, accessibility, seeding, and serial Aspire execution rules."
---

# Browser testing

Use the common testing instructions plus these browser-specific constraints. The detailed recipe
is `.agents/skills/nova-testing/references/browser-suite.md`.

## Browser suite (`Nova.Browser.Tests`)

Automated, committed browser coverage of real UI flows against the Aspire-hosted app (Playwright
Chromium + `Microsoft.Playwright`). It reuses `NovaAppHostFixture`, `IdentityHttpClientHelper`,
and `SeedingHelpers` from `Nova.Integration.Tests` via a project reference and
`InternalsVisibleTo("Nova.Browser.Tests")`.

Hard-won facts that are not discoverable from the code — follow them or you will rediscover each by failing:

- Playwright assertions live in the static `Microsoft.Playwright.Assertions` class
  (`using static Microsoft.Playwright.Assertions;`); there is no bare `Expect(...)`.
- Blazor performs `NavigationManager.NavigateTo` client-side (no document load fires): use
  `WaitUntilState.Commit` for `WaitForURLAsync`/`GoBackAsync`/`GoForwardAsync`, never the
  default `Load`.
- SSR-prerendered roster rows swallow clicks until the interactive circuit attaches: always
  click-through with a retry helper that stops once the drawer actually opens (see
  `OpenParticipantAsync` in `CampaignEvaluationBrowserTests`), and open+close a participant
  before driving filters in URL-state tests to prove hydration.
- **Playwright action methods throw `System.TimeoutException`, not `PlaywrightException`,** on
  actionability timeouts (Click/Check/Select/Fill/Focus). Hydration-retry loops that catch only
  `PlaywrightException` let these escapes turn latency into hard failures. Catch both:
  `catch (Exception e) when (e is PlaywrightException or TimeoutException)` — the pattern
  `InteractionHelpers.ActUntilAsync` already applies. `Expect`-only assertion catches should stay
  `PlaywrightException` (assertion failures use that type).
- **Never assert computed styles synchronously after triggering a CSS transition.** Bootstrap
  transitions (e.g. `box-shadow` ~0.15s, or an async-loading stylesheet) mean a single
  `getComputedStyle().backgroundColor`/`.boxShadow` read right after `focus()` observes the start
  value. Poll through `BrowserRetryPolicy` (read, break when it matches, else
  `WaitForTimeoutAsync(BrowserRetryPolicy.Delay)`).

Conventions:

- One-time setup per machine: `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`
  (relocate the cache with `PLAYWRIGHT_BROWSERS_PATH` if needed).
- Env-gated helpers must `Assert.Skip(...)` when their flag is unset, never pass silently:
  `NOVA_BROWSER_HEADED=1` shows the browser, `NOVA_A11Y_SCREENSHOTS=1` captures accessibility
  evidence under the current verification run’s `browser/<test>/screenshots` directory (screenshots + contrast/touch-target measurements).
- Accessibility regression assertions (drawer control touch targets ≥24×24 px, tag chip text
  contrast ≥4.5:1) belong in this suite; keep them in the scenario that exercises the control.
- Seed users via `IdentityHttpClientHelper` (HTTP registration) and data via the fixture's
  admin EF context — never through UI registration in automation; sign in through the real
  `/Account/Login` page.
- New shared seeding helpers go in `Nova.Integration.Tests\Http\SeedingHelpers.cs` (internal,
  visible to the browser project); do not copy seeding helpers per file.
- Hydration-retry windows are centralized in `Nova.Browser.Tests\BrowserRetryPolicy.cs`
  (env-tunable `NOVA_BROWSER_RETRY_MAX_ATTEMPTS` / `NOVA_BROWSER_RETRY_DELAY_MS`) and consumed by
  `Nova.Browser.Tests\InteractionHelpers.cs`; do not reintroduce per-file hard-coded
  `ActUntilAsync`/`ClickUntilAsync`/`TabUntilFocusedAsync` copies. Defaults and the Azurite/upload
  seeding-retry bounds live in `.agents/skills/nova-testing/references/browser-suite.md`.
- The AppHost fixture (`Nova.Integration.Tests\Data\NovaAppHostFixture.cs`) best-effort waits for the
  Azurite `storage` resource to report healthy and retries the `profile-photos` container probe
  through a bounded hard-coded window before failing fast; `IdentityHttpClientHelper` retries the
  profile-photo upload on transient failures (transport errors / 5xx) with a fresh multipart payload
  per attempt.
- **Machine-scoped serialization**: Aspire-backed integration and browser runs share the machine's single Docker engine, so a concurrent run from another worktree can push this suite's bounded retry windows (`BrowserRetryPolicy`, the fixture's Azurite probe) into flaky timeouts — wait for the other run instead. The runs' identities are already isolated: the testing builder randomizes host ports (`DcpPublisher:RandomizePorts=true` default), DCP appends per-run random suffixes to container and session-network names, the fixture strips all data volumes (`RemoveDataVolumes`), and dev-run volumes hash the checkout path — so the shared piece is capacity, not names or ports.
- The full step-by-step recipe lives in `.agents/skills/nova-testing/references/browser-suite.md`.
