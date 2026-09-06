> **Test gate**: before opening this PR, all three test suites must pass locally.
> On pushes to this PR, re-run the suites the change can affect (unit always;
> integration for provider/HTTP-boundary or EF changes; browser for interactive UI,
> markup, or JS-interop changes) — when in doubt, run all three, and re-run all three
> before merge. CI only builds and runs unit tests, so a green CI run is not proof the
> full suite is green. See `AGENTS.md` → "Build & validation".

## Summary

<!-- What does this change and why? Link issues or design docs if relevant. -->

## Validation

<!-- Keep one validation record. Identify the tested revision (or commit plus
     explicitly listed later docs-only changes), exact commands/results, and any
     unavailable checks. Link durable evidence instead of repeating test counts
     in several files. Existing unaffected-suite results on intermediate pushes
     must name their tested revision; all three suites must pass before merge. -->

- Tested revision:
- Guidance and behavior: <!-- Sources actually read; relevant transitions and sibling paths checked. -->
- Commands and results:
- Separate local review: <!-- Reviewer/session and disposition for substantial
  auth, async ownership, recovery, HTTP/provider, or cross-component changes;
  otherwise explain why focused self-review is sufficient. -->
- Unavailable checks or remaining limitations:

## Checklist

- [ ] Format check passes: `dotnet format Nova.slnx --verify-no-changes`
- [ ] Unit tests pass: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`
- [ ] Integration tests pass: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` (local, requires the Aspire AppHost)
- [ ] Browser tests pass: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` (local-only, Playwright)
- [ ] Applicable guidance and related implementations were checked; substantial changes received a separate local review and findings were resolved by behavior/evidence, including suppressed review-body findings.
- [ ] If `Nova/scss/` or `Nova/package.json` changed: `npm run build:css` and `npm run check:contrast` pass (run from `Nova/`)
