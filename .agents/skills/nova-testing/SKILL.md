---
name: nova-testing
description: >-
    Write, run, or review Nova tests and behavioral evidence. Choose pure-policy tests, bUnit,
    SQLite tenancy tests, Aspire PostgreSQL integration tests, or the Playwright browser suite
    and run them on Microsoft.Testing.Platform. Use for corrected form retries, async ownership,
    authentication changes, recovery, HTTP contracts, provider/lifecycle races, migrations, and
    browser interactions. Feature and persistence work starts with its dedicated recipe and
    invokes this skill for verification.
---

# Nova Testing

Use this skill when writing, running, or reviewing Nova tests and behavioral evidence. Read the
relevant reference before editing tests or assessing what their outcomes prove:

- [Unit SQLite tenancy harness](references/unit-sqlite-harness.md) for `Nova.Unit.Tests`, shared in-memory SQLite, `TenancyTestHarness`, `FakeCurrentUserProvider`, and `ActAs`.
- [Blazor component tests](references/blazor-component-tests.md) for bUnit + NSubstitute component
  rendering, `EventCallback` assertions, the required render-mode assertion, and persisted-state
  restore coverage.
- [Aspire integration harness](references/aspire-integration-harness.md) for `Nova.Integration.Tests`, real PostgreSQL 18 via Aspire AppHost, `NovaAppHostFixture`, HTTP e2e, and provider-specific checks.
- [Browser suite](references/browser-suite.md) for `Nova.Browser.Tests` — Playwright against the
  Aspire-hosted app, the browser fixture and seed, Blazor attachment/navigation pitfalls, and the
  accessibility regression conventions.
- [Aspire + Playwright validation](../aspire-playwright-validation/SKILL.md) for one-off manual
  browser acceptance passes; for committed regression coverage, add a `Nova.Browser.Tests`
  scenario instead.

## Select evidence

Read [Testing Rules](../../../.github/instructions/testing.instructions.md) for project/harness
selection and shared conventions. Before adding tests, use the
[transition evidence guide](references/transition-evidence.md) to select the changed behavior,
expected outcome, and boundary that proves it. Reuse existing tests where they already prove the
claim. Keep this small list and its results in the single validation record required by
[AGENTS.md](../../../AGENTS.md#completion-and-review).

## Run tests

[AGENTS.md](../../../AGENTS.md#build--validation) owns the full commands, build order, serialization,
and opening/intermediate/final PR gates. All three projects use xUnit v4 on MTP; use the explicit
`--project` form after building, with `--no-build`. Bare positional csproj invocation can fail discovery.

For a targeted run, append `--filter-class "*Name"` to the relevant command. Repeat
`--filter-class` for multiple classes; do not join them with `|`. Do not pass VSTest-only flags
(`--nologo`, `--collect`, `--logger`); MTP rejects them.
