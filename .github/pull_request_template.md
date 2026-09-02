> **Test gate**: before opening this PR, all three test suites must pass locally.
> On pushes to this PR, re-run the suites the change can affect (unit always;
> integration for provider/HTTP-boundary or EF changes; browser for interactive UI,
> markup, or JS-interop changes) — when in doubt, run all three, and re-run all three
> before merge. CI only builds and runs unit tests, so a green CI run is not proof the
> full suite is green. See `AGENTS.md` → "Build & validation".

## Summary

<!-- What does this change and why? Link issues or design docs if relevant. -->

## Checklist

- [ ] Format check passes: `dotnet format Nova.slnx --verify-no-changes`
- [ ] Unit tests pass: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`
- [ ] Integration tests pass: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` (local, requires the Aspire AppHost)
- [ ] Browser tests pass: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` (local-only, Playwright)
- [ ] If `Nova/scss/` or `Nova/package.json` changed: `npm run build:css` and `npm run check:contrast` pass (run from `Nova/`)
