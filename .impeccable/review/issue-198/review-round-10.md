# Review round ten — validation order and relative dimensions

Base `107823225aeb7d23f3fbce40402fe511fd2c63b8`; remote Build and Unit Tests passed.
Review `5182209381` says “Needs a closer look” and contains seven suppressed CSS
findings. It is not the user's clean stopping exception. Complete collection covered
30 reviews, eight issue comments and 19 resolved threads, with no additional page.

Session `8083439a-1821-4f0b-853a-995d36588fbf` completed. Its raw log and events expose
seven CSS comments, while workflow `34632189507` stores ten findings and classifies
all ten (seven nit, three moderate). The workflow also reports an ensemble-finalizer
table mismatch and deterministic fallback. No Actions artifacts expose more wording.
The three additional locations are `CampaignEvaluationQueryService.cs:22,58,96`;
their full comment text is unavailable. The source-based assessment below is explicit
about that limit rather than inventing what those comments said.

- All seven CSS locations are corrected in the evaluation panel, placement context
  and note item: 44px targets become 2.75rem. Related 48px controls become 3rem and
  3px focus lengths become .1875rem. Geometry is equivalent at the existing 16px root
  and scales with root text size. Hairline borders and viewport breakpoints retain px.
- Each of the three service locations created a context before validating input.
  Mandatory validation rules require the reverse order. All three public reads now
  validate their own input before the factory; the shared access helper retains
  persisted membership, participant visibility and capability checks. SQL paging,
  response contracts and context disposal are unchanged. Participant/effective-placement
  sibling services already follow this ordering.
- Twenty focused unit rows cover invalid IDs across all reads and malformed cursor
  pairs across both histories. They require structured field errors and zero factory
  calls, with a factory configured to fail if reached. Existing valid-path coverage remains.

Guidance actually read/reused: AGENTS; C#, Blazor, UI-design, validation, service,
API, tenancy and testing instructions; add-feature-slice service/input references,
add-blazor-ui, nova-testing and its browser/component guidance, installed run-tests;
Impeccable incumbent-world, hardening and craft-floor guidance. No new composition,
token, instruction or skill is needed for established conventions. The
[separate review](review-round-10-local-review.md) records its independent assessment.

Source is the base plus five files, manifest SHA-256
`2C1A33BD1430653A2725748247B0E374CA3B9837799A57659287BB9933ED8E83`
(`.impeccable/archive/round10-source-manifest.log`). Whitespace formatting passed.
`dotnet build Nova.slnx` passed with zero warnings/errors in 2m 57.40s. Full unit
(`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`) passed:
3,153 tests, zero failures/skips, 55.366s. Full PostgreSQL (`dotnet test --project
Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`) passed: 608 tests,
zero failures/skips, 13m 55.906s. The first full browser run finished 175/176 in 21m 27.385s: login navigation timed out during confirmed Windows idle Modern Standby (Kernel-Power 506/507, 14:00:05–14:16:21 local). No tested reopen behavior was reached in that case. The failed run and power events are retained locally; a full unchanged rerun uses a temporary system wake request, released after validation. No timeout, assertion or application change was made for this interruption. The unchanged full browser rerun passed all 176 tests, zero failures/skips, in 3m 51.383s (including the interrupted reopen case). It used `--no-build --output Detailed --long-running 90 --xunit-diagnostics on`, with `NOVA_A11Y_SCREENSHOTS=1` and a separate `NOVA_EVALUATION_EVIDENCE` directory. Full `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` passed (0 of 947 files changed; 66,606ms).
Fresh desktop (1440×1000) and phone (390×844) captures were visually inspected: the established sheet, focus, wrapping and separate phone stage remain intact. Responsive/contrast/target tests pass; the separate 1,000-participant lookup returned 20 rows with 999 matches in 1,522.9683ms (no specified latency threshold). Raw logs and repeated captures remain ignored local artifacts. The [complete disposition](https://github.com/eruvalca/Nova/pull/253#issuecomment-5639629536) was posted before the single combined push; wait for fresh
CI and automatic review without requesting one. No merge is authorized.
