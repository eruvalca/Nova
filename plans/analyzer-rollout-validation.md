# Analyzer rollout validation

Status: complete through IDE1006 naming enforcement. The full solution build, formatting, and all three test suites pass. Earlier diagnostic-build results below are historical.

## Implementation and architectural decisions

- Preserved the user's analyzer rollout and existing staged/uncommitted work. No files were
  staged or committed by the agent, and the existing MA0048 setting was retained.
- Kept Nova.SharedKernel and aligned renamed Common directories and namespaces in Nova.UI,
  Nova/Features, and Nova/Components/Account. No old Nova.Shared or renamed Shared namespace
  references remain in source or agent guidance.
- Used C# extension blocks following Nova.ServiceDefaults/ServiceDefaultsExtensions.cs.
- Moved HttpSuccessContentExtensions to Nova.SharedKernel/Results, next to the shared HTTP
  error-response helper. It depends only on HTTP/JSON and shared result contracts, with no UI
  or client dependency. Strict required-constructor/nullability checks, custom validators,
  cancellation propagation, and malformed-success handling are preserved. Existing caller and
  HTTP contract tests pass, including the helper tests now under Nova.Unit.Tests/Results.
- Added Nova.Analyzers/LocationlessPublicTypeSuppressor, referenced as an analyzer by the server.
  It suppresses CA1515 only when Location equals Location.None. Regression tests prove that
  source-located CA1515 and unrelated diagnostics remain unsuppressed.
- The requested clean succeeded and a normal rebuild reproduced the locationless diagnostic.
  A temporary classic-extension comparison confirmed the cause and was reverted. The final
  implementation retains extension blocks and server-only mappings remain on the server.

## Exception and enforcement review

- The normal analyzer configuration and warnings-as-errors remain enabled. There is no global
  CA1515 exclusion. Documented source exceptions cover deliberate framework patterns: public
  Razor/Identity types, string-bound routes, renderer context, synchronous UI cancellation,
  translated EF casing, generated migrations, and cohesive operations/test scenarios.
- Follow-up: test names now use SubjectOutcomeCondition (PascalCase), and the test-scoped CA1707
  suppression was removed. Async test methods now also use the required Async suffix; the
  test-project accessibility override has been removed.
- Ownership-transfer exceptions are local: multipart parents dispose their child content, returned
  clients own their handlers, and factory callers dispose returned contexts. Actual missing
  disposal was fixed rather than suppressed. Racing HTTP tasks now finish together, and a
  successful response is disposed even if its peer fails. Setup-created clients are disposed
  on setup failure. Returned HTTP response factories remain alive for their callers.
- No tests were disabled or weakened. All seven optional browser evidence cases were explicitly
  enabled in the final full browser run; none were skipped.

## Defects found during validation

- Newly internal EF context types needed signed DynamicProxy access for the existing NSubstitute
  factories. Added the required friend assembly; the affected service tests pass.
- Wrapped Razor text changed exact labels consumed by existing component tests. Restored
  continuous labels and enrollment-preview text without changing their wording.
- The profile-photo Save action could start before Cropper.js finished loading the selected image.
  The browser case failed in the full suite but passed in isolation. The editor now waits for its
  ready event, matching both crest editors, resets readiness for a new selection, and guards
  duplicate/busy saves. A component regression verifies readiness across two selections; the
  full browser suite now passes. Added bounded failure diagnostics to the browser case.

## Final test evidence — 2026-09-08

Commands ran serially; tests used --no-build after a completed solution build.

| Check | Result |
| --- | --- |
| dotnet build Nova.slnx --nologo | Passed, 0 warnings and 0 errors |
| dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build | 2,562 passed, 0 failed, 0 skipped; 16.3s |
| dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build | 531 passed, 0 failed, 0 skipped; 1m 14s |
| NOVA_A11Y_SCREENSHOTS=1; dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build | 127 passed, 0 failed, 0 skipped; 3m 33s |
| dotnet format Nova.slnx --verify-no-changes --no-restore | Passed, no changes required |
| git diff --check | Passed |

The final runtime suites include the profile-photo readiness fix. Subsequent cleanup removed
extra blank lines at EOF only; the post-cleanup full build and formatting verification both passed.
A premature test start during an earlier build caused output-file locks; that attempt and its
stale-binary results are excluded from the evidence above.

Logs retained locally: build-analyzers.log, test-unit-analyzers.log,
test-integration-analyzers.log, test-browser-analyzers.log, format-verify.log, and diff-check.log.
Accessibility screenshots and measurements use the suite's existing temporary output directory.
All temporary repository fix scripts and the exploratory client SARIF file were removed.

## Instruction sources read

- `AGENTS.md` and applicable `.github/instructions/*.instructions.md` (C#, Blazor, service,
  API, validation, tenancy, lifecycle, placement, testing, functional core, observability, UI/theme).
- `.agents/skills/nova-testing/SKILL.md` and its unit/SQLite, component, integration, and browser references.
- `.agents/skills/add-blazor-ui/SKILL.md` and placement, render-mode, lifecycle/state, parameter,
  form, and JS references.
- `.agents/skills/add-feature-slice/SKILL.md` and its references.
- `.agents/skills/add-api-endpoint/SKILL.md` and its references.
- `.agents/skills/add-domain-persistence/SKILL.md`, retrying mutations/locks, and query-construction references.
- `.agents/skills/aspire/SKILL.md` was inspected; its ordinary build/test exclusion applies to this work.

No changes have been staged or committed by the agent.

## CA1707 test naming follow-up (2026-09-08)

- Removed the test-project CA1707 suppression and renamed 2,496 test method declarations
  to PascalCase: 1,885 unit, 487 integration, and 124 browser methods. Updated references
  and shared agent guidance; assertions, test data, and discovery attributes are unchanged.
- Kept the separate existing public-test Async-suffix exception unchanged.
- Build: zero warnings and errors. Formatting verification and git diff --check pass.
- Full suites: 2,562 unit, 531 integration, and 127 browser tests passed; none skipped.
  Browser run enabled NOVA_A11Y_SCREENSHOTS=1.
- Initial unit run had one timing-sensitive failure in
  NewCampaignIgnoresLateInputStorageFailureAfterClubChangesAsync. Its class passed in isolation
  and the complete unit suite passed on rerun without source changes.
- Guidance consulted: csharp-conventions.instructions.md, testing.instructions.md,
  nova-testing/SKILL.md and its unit-sqlite-harness, blazor-component-tests,
  aspire-integration-harness, and browser-suite references.
- Nothing staged or committed.

## Async test naming follow-up (2026-09-08)

- Removed the remaining test-project async_method_symbols accessibility override and its
  now-empty EditorConfig section. The global Async-suffix rule applies to public tests.
- Appended Async to affected async method names, updated references and shared naming guidance,
  and preserved test data, attributes, and assertions. Synchronous tests retain their names.
- Full solution build: zero warnings/errors. All suites pass: 2,562 unit, 531 integration,
  127 browser; none skipped. Browser accessibility evidence was enabled.
- Rebuilt and reran the complete unit suite after restoring unrelated preview variable names
  caught during the rename audit; both passed. Integration/browser sources were unchanged.
- Nothing staged or committed.

## Server accessibility audit after suppressor removal (2026-09-08)

- Made five standalone receipt entities internal: TagDefinitionMutationReceiptEntity,
  EvaluationNoteMutationReceiptEntity, PlacementMutationReceiptEntity,
  ClubMembershipMutationReceiptEntity, and PlayerImportReceiptEntity. Removed their
  unnecessary CA1515 suppressions, plus the redundant suppression on the already-internal
  ITenantOwnedEntity. No entity properties or EF mappings changed.
- Remaining public source types are required by existing Razor/Identity constructor contracts,
  component parameters, or their transitive public entity navigation contracts.
- Removed obsolete tests and direct Roslyn package dependencies for the deleted suppressor.
- A temporary experiment making server extension members internal, in addition to their
  already-internal containers, still reported the locationless CA1515 error; reverted it.
- Current server build reports only locationless CA1515. Tests have not been rerun against
  these changes because the build is blocked. User requested retaining extension-block syntax; the remaining build error is unresolved.

- Confirmed in an isolated temporary project with Nova's AnalysisLevel=latest and AnalysisMode=All:
  internal Program + internal extension container + internal extension member still produce
  locationless CA1515. Equivalent classic syntax passes. With default analysis mode, the
  extension-block probe passes; matching Nova's analysis configuration is essential.
- Temporary symbol diagnostics identify public ExtensionBlockDeclarationSyntax symbols with
  empty identifier locations inside internal server extension containers. No probe analyzer
  or syntax workaround was added to the repository. User chose to retain extension blocks.
- Formatting verification and git diff --check passed. Build remains blocked as described.

## Final exhaustive accessibility review (2026-09-08)

- Reviewed 352 declared Nova types, including nested types and migrations: 64 public,
  209 internal, 50 private, and 29 implicitly internal. The 57 migration types are internal
  or implicitly internal. The public set contains 42 Razor components and 22 contract-required
  server types. No further safe type-accessibility reductions were identified.
- Confirmed InternalsVisibleTo for Nova.Unit.Tests, Nova.Integration.Tests, Nova.Browser.Tests,
  and DynamicProxyGenAssembly2. No additional friend assemblies were necessary.
- Existing source suppressions preserve public Razor/Identity and navigation contracts.
  Isolated probes show type/file suppressions cannot suppress the locationless CA1515,
  and an assembly suppression also hides legitimate source-located CA1515. The user explicitly
  chose to leave the remaining build error unresolved; no assembly suppression was added.
- Latest stable Microsoft.CodeAnalysis.NetAnalyzers 10.0.400 also reproduces the issue in
  a temporary project. No analyzer dependency was added or upgraded in the repository.
- Diagnostic validation build uses /p:WarningsNotAsErrors=CA1515 for that invocation only:
  one CA1515 warning, zero errors. This is not a passing build under normal repository rules.

- All suites passed against the diagnostic build: 2,559 unit, 531 integration, 127 browser;
  zero failures/skips. Browser accessibility evidence enabled via NOVA_A11Y_SCREENSHOTS=1.
  The unit count excludes the three obsolete tests for the removed suppressor.
- No files staged or committed. Normal build still fails on the known locationless CA1515,
  deliberately left unresolved per the user's decision.

## Approved Nova-only EditorConfig suppression

- Added [Nova/**.cs] with dotnet_diagnostic.CA1515.severity = none in the root .editorconfig,
  as requested after verifying this configuration resolves the locationless diagnostic.
- This disables CA1515 for Nova source files, including future public types; other project
  folders keep their existing enforcement. No assembly suppression or custom suppressor added.
- Correction to earlier investigation: source pragmas/type attributes could not suppress the
  locationless diagnostic, but disabling the rule for all Nova source files via EditorConfig works.
- Normal dotnet build Nova/Nova.csproj: zero warnings, zero errors. No test rerun for this
  configuration-only change; the preceding accessibility test run passed all three suites.

## Solution-wide ordinary-await cleanup

- Disabled CA2007 in root EditorConfig and removed the rollout-added ConfigureAwait calls
  and local CA2007 pragmas across the solution. The committed baseline contained no such calls.
- Also disabled MA0004, whose equivalent ConfigureAwait requirement otherwise failed the
  shared HTTP helper build after removal. Other analyzer rules remain enabled.
- Restored ordinary await, await foreach, and await using, merging rollout disposal aliases
  and removing redundant parentheses. Preserved disposal scopes, cancellation arguments,
  assertions, and resource ownership. Updated shared C# guidance to prevent reintroduction
  solely to satisfy these disabled rules.
- Disposal audits checked all 391 test aliases, 152 server aliases and three scoped server
  conversions, plus the AppHost scopes. Corrected one missed await using found by CA2000
  before final verification; no other missing ownership was found.
- Final verification: dotnet format --verify-no-changes passes; full solution build has
  zero warnings/errors; git diff --check passes. Tests: 2,559 unit, 531 integration,
  127 browser passed with no skips. Browser accessibility evidence was enabled.
- No source ConfigureAwait calls or local CA2007 pragmas remain. Nothing staged or committed.

## IDE1006 naming enforcement

- Kept the user-enabled IDE1006 error severity. Corrected 242 distinct symbols reported at
  243 diagnostic sites: private-field prefixes, PascalCase constants, and Async suffixes.
- Updated C# references, companion Razor expressions/form callbacks, endpoint registrations
  inside extension blocks, and the two reflection-based season-handler test lookups.
- No naming suppressions, framework-contract renames, or wire-value changes were introduced.
- Full solution build passes with zero warnings/errors. All 2,559 unit tests pass without skips.
- Final verification: 531 integration and 127 browser tests pass with no skips; browser accessibility evidence enabled. Full formatting verification and git diff --check pass.
- Applied the existing C# conventions, API and Blazor rules, testing instructions, and
  nova-testing recipe/reference guidance recorded above. Changes remain unstaged/uncommitted.