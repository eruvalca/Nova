# Round twelve — protect unreadable recovery and failed discard

Base `f44b1d6659f1069e4fa72048a9775f8ed19d13c8` passed both CI checks. Review
`5183159432` contains four suppressed findings; all are addressed. Collection covered
32 reviews, ten comments and 19 resolved threads, with no further pages. Session
`a14e082b-36d8-4740-ab81-c20ca49a9114` and workflow `34643325360` were inspected:
nine stored events map to five locations, and the classifier reports five findings
(four moderate, one nit). Raw logs/events expose only the aria-current finding.

- Selected result links now use `aria-current="page"`.
- Evaluate saves the empty discard snapshot before clearing visible add/edit/trait
  text. Failed writes preserve copyable fields and the reviewed note version. Owner,
  request and storage revision checks reject stale completion and error feedback.
- Unreadable recovery protects Evaluate form/code navigation and drawer Close,
  Previous, Next and NavigationLock. Protection remains during a recovery retry.
  Explicit retained-data departure preserves bytes; known pending submissions block
  movement. Scoped permission lets the confirmed navigation pass. Drawer callbacks
  rearm the native guard if the same owner remains, including a no-op parent move.
- The fifth workflow-only location is cleanup lines 49–51; full wording is unavailable.
  Independent inspection finds it inapplicable to production: Npgsql filters/orders/
  limits 500 expired receipts in SQL with the expiry-leading index. Materialization
  belongs to the documented SQLite DateTimeOffset fallback, also used by import
  cleanup. No backend change is needed; this limitation is disclosed on the PR.

Guidance read/reused: AGENTS; Blazor, C#, UI-design, validation, service/API, lifecycle,
tenancy and testing rules; add-blazor-ui lifecycle/interop, domain retention and
nova-testing component/browser references; code-testing-agent and run-tests. Existing
composition and design decisions remain applicable. No new permanent rule or skill is
justified. [Separate local review](review-round-12-local-review.md) records the actual
diff, sibling checks and a framework-source hypothesis withdrawn after verification.

Thirteen component cases cover form/code/drawer entry, held reads, both write-failure
modes, simultaneous drafts/edit version and newer draft/owner races. The browser
`UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync` matrix now begins
with actual search-form submission, then verifies explicit native departure, retained
bytes, Back and no mutation in ordinary Auto and positively verified WASM execution.

Validated source is the base plus nine changed source/test files. Initial manifest
SHA-256 `D4C99D54DDB7F1FCBAB6ABC216492FCDB23D1765E41CD4CCB48E226B146149F8`
identifies the unit/browser run. Format verification found one missing UTF-8 BOM;
after browser completion, exactly those three bytes were added to Drawer.Recovery.cs,
with all original bytes verified identical. Final manifest SHA-256
`DD8D0D27288F9FCDA1DC780E9288C776C9B0DB7CD601D9989E398BBC9E18D02C`
identifies the rebuilt/integration-validated source. Only documentation follows.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Final build: 0 warnings/errors, 7.05s |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignEvaluationPanelTests' --filter-class '*CampaignParticipantDrawerTests'` | 284 pass, 2.508s |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,172 pass, 15.439s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --output Detailed --long-running 90 --xunit-diagnostics on` | 183 pass, 4m11.923s; accessibility/captures enabled |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Fresh PostgreSQL: 608 pass, 1m42.765s |
| `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` | 0/949 files changed, 99,550ms, exit 0 |

Every final test pass has zero failures/skips. Aspire suites ran serially; the temporary
system wake request is released after execution. Fresh desktop/mobile captures were
inspected; the established fixed-navbar full-page capture behavior is unchanged.
Initial CA1508 and duplicate-cancellation failures were corrected in production; the
first targeted run was 283/284. No assertion, check or suppression was weakened.

The [PR disposition](https://github.com/eruvalca/Nova/pull/253#issuecomment-5640533297)
was posted before the one combined commit/push. Fresh automatic review and CI
remain required afterward; no review is requested manually and no merge is authorized.
