# Build quality enforcement

Authorized 2026-09-06: implement the compiler/analyzer recommendations from the article review,
excluding the proposed review skill. Continue the existing setup-maintenance PR. Preserve the
shared Codex/Copilot layout, current packages/runtimes, framework contracts, and scoped exceptions.

## Phase 1 — Measure and configure

Status: Complete for the bounded profile; naming migration explicitly deferred below.

- [x] Repair incomplete naming styles and the missing constants style.
- [x] Establish a diagnostic baseline with build-time style analysis and selected SDK reliability
  checks; keep test naming and framework-owned members valid.
- [x] Enable warnings-as-errors and the selected checks with bounded, reviewed fixes.

Verification: inspect actual compiler output and effective project settings. Record diagnostic IDs,
scope decisions, and fixes; do not treat configuration presence as proof of enforcement. Do not
enable AnalysisMode=All, install overlapping analyzer packages, or perform an unrelated code sweep.

## Phase 2 — Guidance and enforcement proof

Status: Complete.

- [x] Add concise shared guidance for justified, reviewed exceptions to quality controls.
- [x] Document the implemented build checks and their limits in the existing setup guide.
- [x] Use disposable examples to prove expected diagnostic failures and corrected passes, including
  valid tests/framework contracts. Keep all fixtures outside application source.

Verification: use actual repository configuration and the installed SDK, inspect diagnostic IDs,
and distinguish mechanical checks from behavior such as component lifetime or rendered retries.
No new review skill, custom agent, framework, dependency, or personal-configuration change.

## Phase 3 — Review and delivery

Status: Local verification and publication complete. Current CI disposition is recorded in the PR.

- [x] Obtain separate review of the full diff, diagnostic dispositions, and enforcement evidence.
- [x] Run the solution build, formatting verification, and unit tests sequentially. Run affected
  integration/browser suites with --no-build and respect machine-wide Aspire serialization.
- [x] Verify guidance parity and clean fixture/artifact boundaries.
- [x] Commit, push the existing PR, and update its validation record with the tested revision and CI.

## Evidence and handoff

Starting revision: `49352487008d1c120182dccbf0b5f3261588358c`; initial working tree clean.
The preceding inspection found SDK analysis already enabled in all nine projects, build style
analysis and warnings-as-errors disabled, five incomplete naming styles, and existing xUnit
analyzers. That inspection did not execute a stricter build; it is not a diagnostic baseline.

2026-09-06 baseline (installed .NET SDK 10.0.301): a solution rebuild with build style enabled and
warnings-as-errors disabled reported IDE1006 naming diagnostics and four CA2012 diagnostics in
two NSubstitute mock arrangements in one test class; no CA2016 diagnostics. The initial test-path
exception incorrectly missed top-level test files.
An isolated compiler probe proved that `/**.cs` matches both top-level
and nested files, while the attempted `/**/*.cs` pattern missed top-level files in this matcher.
The corrected exception excludes public test methods, including public helpers because
EditorConfig cannot select Fact/Theory attributes, but preserves enforcement on non-public helpers.
Probe evidence remains outside the repository under the temporary enforcement-fixture directory.

After correcting the test scope, the strict naming baseline contains 244 distinct diagnostic sites
across 98 C# files (245 build reports because one source is linked into two projects). This exceeds
the agreed bounded setup scope. In addition, existing endpoint recipes teach `*Handler` names while
the repaired generic async naming rule requires an `Async` suffix. A separate reviewer confirmed
that selectively exempting Account or dropping explicit field/constant rules merely to remove this
backlog would be inappropriate. This change therefore repairs the definitions and keeps IDE1006
at suggestion severity. It does **not** deliver build-blocking naming enforcement.

### Deferred naming migration

- [ ] Align the endpoint recipes and their code examples with the intended async naming convention.
- [ ] Rename the reported symbols and every C#/Razor consumer in a separately scoped change; retain
  test conventions, framework-owned names, and wire/form contracts.
- [ ] Promote IDE1006 to warning only after that migration and its behavioral validation pass.

These are explicit follow-up work, not completed acceptance claims for this bounded implementation.
To reproduce the measured backlog in a disposable copy, set IDE1006 to warning in that copy's
`.editorconfig`, then run `dotnet build Nova.slnx --no-restore -t:Rebuild
-p:TreatWarningsAsErrors=false` after restoring dependencies. Inspect diagnostic locations and IDs.
The installed formatter reports that IDE1006 does not support Fix All in Solution; do not assume it
can perform or verify the naming migration. Normal formatting verification retains its default
warning threshold. No maintained-code file inventory or scaffold exemption was added to hide naming
diagnostics.

### Enforced profile and bounded fix

`Directory.Build.props` now enables `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` for all
projects. Existing warning-level build-capable style rules and emitted compiler/analyzer warnings
become failures; `.editorconfig` explicitly promotes SDK CA2012 and CA2016 to warning. This does
not enable every SDK rule or turn suggestions into errors. Existing xUnit analyzers remain in use.

Both mock arrangements now use NSubstitute's `ValueTask<T>`-specific `.Returns(true)` overload,
verified against the installed 6.2.0 API documentation and independently reviewed. The original
values were completed, value-backed tasks: these are two analyzer-compatible mock configurations,
not four demonstrated runtime bugs. Sibling inspection found no other `.Returns(ValueTask.FromResult`
arrangements in the unit suite. No production code, markup, API, or database schema was changed.

The final-profile solution rebuild (`dotnet build Nova.slnx --no-restore -t:Rebuild -v:minimal`)
passed with zero warnings and errors. Logs are outside the repository in
`%TEMP%/nova-build-quality-20260906`; enforcement proof and final test results follow below.

### Enforcement proof

The disposable harness copied the actual `.editorconfig`, `Directory.Build.props`, and `global.json`
byte-for-byte, checked their hashes before/after, and used the installed SDK. Projects referenced
no packages and cleared package sources; restored package-library counts were zero. The harness
checked diagnostic IDs, severities, counts, and exit codes, not just success/failure.

| Fixture | Violating example | Corrected/control result |
| --- | --- | --- |
| Compiler nullability | Returning a nullable string as non-nullable: exactly one CS8603 error, build fails | Non-null return builds without the diagnostic |
| ValueTask consumption | Reusing the ValueTask: exactly one CA2012 error, build fails | Convert once to Task and consume that Task; build passes |
| Cancellation forwarding | Omitting an available last-parameter token: exactly one CA2016 error, build fails | Forward the token; build passes |
| Existing build style | Missing required braces: exactly one IDE0011 error, build fails | Add braces; build passes |
| Naming definitions | Interface, type parameter, async method, private field, private constant, and local constant: six IDE1006 notes, build passes | Compliant names produce no naming notes |
| Test-project scopes | Private async helper and field remain advisory in top-level and nested files of all three test projects | Public test names remain valid; corrected helper/field names remove notes |
| Fixed contracts | Inherited/implemented framework names remain valid | No IDE1006 on fixed-name members or a non-async Task-returning control |

All 24 final cases passed. One initial naming fixture accidentally used a lowercase-only type
parameter and also triggered compiler CS8981; changing it to `Value` retained the intended missing
`T` prefix without that unrelated diagnostic. Only that fixture was rerun. The initial failure,
input manifest, and corrected run are retained in `%TEMP%/nova-enforcement-fixtures-fdb67e662e2a47d3917f96c02e0c74c1`.
Naming proof establishes advisory detection, not a build gate. The fixed-member examples do not
establish that every reflection-based framework convention is automatically recognized.

### Guidance ownership and review

The quality-control rule lives once in root `AGENTS.md`, because suppressions and disabled CI/config
checks must be covered even when no C# file changes. The setup guide links to it. No review skill
was added. These build checks run through the same `dotnet` commands for Codex and Copilot; this
follow-up does not claim new native CLI instruction/hook execution evidence or close the limitations
recorded in `plans/agent-quality-and-compatibility.md`.

Sources consulted: root guidance; C#, testing, API, service, tenancy, and Blazor/UI scoped rules for
the initial diagnostic owners; `nova-testing` and its harness references; `add-api-endpoint` with
handler/result and route references; `add-blazor-ui` with parameter/event-binding guidance for
classifying owned callbacks versus fixed contracts; and the plan-tracking skill. Baseline review
found the endpoint-recipe/naming conflict above without changing application behavior.

### Final repository checks

The configuration and mock changes were frozen for these checks. Later guidance/evidence edits do
not change the compiled inputs. All dotnet operations and Aspire suites ran sequentially.

| Check | Result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore -t:Rebuild -v:minimal` | Passed, zero warnings/errors |
| `dotnet format Nova.slnx --verify-no-changes --no-restore` | Passed |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed: 2558, no skips |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed: 531, no skips |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Passed: 120; seven existing opt-in screenshot/evidence skips |
| `npm run check:contrast` from `Nova/` | Passed |
| `scripts/Test-AgentGuidance.ps1` and `-SelfTest` | Passed parity and negative fixtures |
| Separate review, links, and diff/artifact check | Passed; no material finding in the final bounded change |
| Current-head CI | Runs on the published revision; see the PR validation record for its latest disposition |

The reviewer independently checked the complete diff, source/configuration hashes, exact fixture
results, mock semantics, naming-baseline counts, and shared-rule ownership. Review removed an
unverified formatter-report instruction in favor of the measured disposable-copy baseline
procedure. A routing pass moved the quality-control rule from C# scope to root guidance so it also
covers CI/config-only changes. Both corrections were re-reviewed. The six-file final change adds
no dependency, runtime, hook, custom agent, review skill, or seeded application defect.

Primary implementer owns configuration, application fixes if needed, this plan, and all build/test
scheduling. Separate workers own the exception/setup wording and disposable diagnostic fixtures;
an independent reviewer checks the final result. Do not run build-capable dotnet commands in
parallel. Resume with the current phase and recorded diagnostics after any interruption.

## Deployment

No application deployment or schema/API migration is intended. Deliver through the existing PR;
any substantial behavioral fix discovered during the baseline must receive its own behavioral
evidence and separate review. Preserve existing pre-merge all-suite gates.

## Delivery handoff

Implementation and local evidence were committed as `0f6abca195f730f9723d58b432d715763d04b3f2` and
pushed to [PR #249](https://github.com/eruvalca/Nova/pull/249). Its validation record owns the latest
tested revision and CI result, including this evidence-only handoff update; the compiled inputs
and enforcement configuration are unchanged. All required local checks passed and the separate
review is clear. No additional implementation remains within the bounded profile.

The naming migration above remains deferred, and the original compatibility plan retains the
unverified native CLI/hook outcomes. Neither is silently treated as complete. Use the existing
pre-merge gate when this PR is ready to merge.
