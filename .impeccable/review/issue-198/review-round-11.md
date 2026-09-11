# Round eleven — leave unreadable recovery data intact

Base `397ccf1f394a608707a74dd5d60abb590a416af2` passed both CI checks. Review
`5182803975` contains one suppressed recovery/navigation finding. Session
`0c8d84f9-efef-48db-8f71-92a6d38eb909` completed; workflow `34639245930` records
exactly one stored location and one moderate classified finding. Raw session/events
expose no stored comments from their ensemble member; the posted review supplies the
full finding. Collection covered 31 reviews, nine comments and 19 resolved threads;
no further thread/comment pages exist. Suppression does not resolve the issue.

Malformed or blocked storage conservatively leaves native navigation pending, while
the old discard handlers reject unavailable storage. The same trap exists in the
Roster drawer after malformed operation parsing. Both now offer an explicit
**Leave and keep recovery data** choice after an actual read failure. Copy explains
the unknown outcome and discarded visible drafts. Departure does not write, delete
or reinterpret retained bytes. A known pending submission still requires recovery.
The shared guard retains its pending flag while permitting the confirmed departure;
cancel, input or renewed pending work revokes that permission. Owner, request and
restore-generation checks prevent late release from navigating newer work.

Guidance read/reused: AGENTS; Blazor, C#, UI-design, testing, lifecycle and tenancy
rules; add-blazor-ui lifecycle/interop references; nova-testing component/browser
references; code-testing-agent and run-tests. Recovery uses the existing composition
and controls. No new design direction, permanent instruction or skill is needed.
[Separate review](review-round-11-local-review.md) covers the actual source and evidence.

Initial builds caught MA0051 (61-line departure method), a redundant CA1508 condition and a test constant naming violation. Routing was extracted, the redundant condition removed and the constant renamed; no suppression was added. The corrected build passed with zero warnings/errors (6.10s). The first focused component run passed 270/271: its new Evaluate race assertion incorrectly counted the deliberate recovery retry write as a departure write. The correction compares the post-retry baseline and verifies the original payload. Final source is the base plus 12 files, manifest SHA-256 `35BEC21DE0D5BC3D3B9A036FD2E7C755B7DB4453B39F8CBAC5486D3F8230B700`. `dotnet build Nova.slnx` passed again after the test correction (0 warnings/errors, 8.80s). Targeted component tests passed 271/271 (2.443s). Full `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` passed 3,159/3,159 (15.718s). Full `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --output Detailed --long-running 90 --xunit-diagnostics on` passed 183/183 (3m33.829s), with `NOVA_A11Y_SCREENSHOTS=1` and fresh responsive captures. All passes have zero failures/skips. Full `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` passed (0/947 files changed, 63,350ms). Temporary Windows system wake protection prevented idle suspension during validation and is released afterward. Source changes are limited to recovery/navigation and their
component/browser regressions. Provider/HTTP/persistence contracts are unchanged;
the 608-test PostgreSQL pass on the base remains applicable for this intermediate push.
All three suites must pass before merge. Rounds nine and ten remain shared at the
base commit with blobs/checksums in [the archive](evidence-archive.md); local copies
are preserved. Initial concept/options and preliminary detector output are also archived at that revision; current approved inputs, finish dispositions and curated captures remain tracked. No code or tests were removed to reduce review size.

The [full disposition](https://github.com/eruvalca/Nova/pull/253#issuecomment-5640120865) was posted before the one combined push; wait for CI and fresh
automatic review without requesting one. No merge is authorized.

| Requirement | Exact regression evidence |
| --- | --- |
| Explicit leave, cancel/retry, no storage mutation or dispatch | `EvaluationUnreadableRecoveryCanKeepWorkingThenLeaveWithoutChangingStoredDataAsync`; `DrawerUnreadableRecoveryCanKeepWorkingThenLeaveWithoutChangingStoredDataAsync` |
| Restore/owner races and known pending protection | `EvaluationUnreadableDepartureCannotOutliveRestorationOrOwnerChangeAsync`; `DrawerUnreadableDepartureCannotOutliveRestorationOrOwnerChangeAsync` |
| Real malformed/blocked-storage journeys, retained bytes and no note | `UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync` (ordinary Auto reload and positively verified WASM) |
| Guard release/cancel, original bytes, pending revocation and history acceptance | `InvalidRetainedCaptureStaysProtectedAndPreservesOriginalBytesAsync`; `HistoryReplayRequiresOwnedUnrevokedAcceptanceAsync` |
