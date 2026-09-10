# Review round 1 — PR #253

The round starts at `63235f98d61d58d4dd80c3f19f057206852ccde4` and addresses all six posted threads and three additional stored findings listed in [the watch record](pr-watch.md). No finding was dismissed because it was withheld, suppressed or outdated.

Tested source fingerprint: SHA-256 `5279745C9FAB4C3C6A599CBFB28EB417BFFD73C4DB2802F476338506CDB6BF61`, computed from `git diff --cached --binary` against that base, scoped to `Nova Nova.UI Nova.Client Nova.SharedKernel Nova.Unit.Tests Nova.Integration.Tests Nova.Browser.Tests`. The commit containing this record carries that tested source; subsequent pre-commit edits only finalize Markdown validation records. The PR validation identifies the resulting commit.

## Changes and boundaries

Retention catches and logs non-shutdown failures around scope creation, context resolution, pruning and disposal. Shutdown cancellation propagates. Drawer identity and trait-choice transport failures use their existing retry states; note/application errors stay regional. Owner and request checks protect both normal startup and prerender restoration from obsolete continuations. Evaluate's three evidence loaders also preserve component cancellation.

Note and trait-application rows require an original `AuthorDisplayName` snapshot. Both production creation paths capture it inside the existing transaction under membership locks. Note edits and duplicate tag application preserve the snapshot and actor ID. Evaluate, the Roster drawer and player history read the snapshots without joining historical actors through current tenant membership. The incremental migration adds required text columns without a fallback, default or backfill, following the repository's no-production-data boundary; populated disposable development databases must be recreated rather than reconciled.

The WASM note client rejects successful edit receipts retaining the expected version, while deletion still requires that version. The placement URL builder omits nonpositive optional participant IDs. Endpoint XML and receipt diagnostics now describe the actual contracts. No design direction, markup, CSS or JS was changed.

## Requirement evidence

| Requirement | Exact test evidence |
| --- | --- |
| Retention failures must not stop the application | `CleanupPassLogsScopeFailureAndAllowsAnotherPassAsync` |
| Preserve shutdown cancellation | `CleanupPassPropagatesShutdownCancellationWithoutWarningAsync` |
| Keep notes and applications independently recoverable | `DrawerRecoversOnlyFailedEvidenceRegionAfterTransportError`; `DrawerIgnoresObsoleteEvidenceFailureAfterParticipantChangesAsync` |
| Preserve identity and catalog recovery | `DrawerRecoversIdentityTransportFailureThroughDetailRetry`; `DrawerRecoversTagChoicesTransportFailureWithoutConcealingEvidence` |
| “Late results, errors, and cleanup cannot affect a newer owner” | `DrawerIgnoresOldTagChoiceFailureAndDoesNotRestartNewEvidenceAsync` covers ordinary and prerender-restored startup |
| Propagate owned cancellation; keep transport cancellation regional | `DrawerPropagatesComponentOwnedCancellationFromEachStartupRegionAsync`; `EvidenceTransportCancellationRemainsRegionalAndRetryable` |
| Reject unchanged edit receipts | `EditRejectsReceiptThatRetainsExpectedVersionAsync`; retained deletion contract tests |
| Keep historical authorship explicit | `NoteCreationSnapshotsAuthorAndEditAndMembershipChangesPreserveItAsync`; `ApplicationCreationSnapshotsActorAndDuplicateAndMembershipChangesPreserveItAsync`; `SharedEvidencePreservesOriginalActorAfterRenameAndDepartureOrDeletionAsync` |
| Emit only accepted optional participant IDs | `PlacementRosterUrlIncludesOnlyPositiveParticipantIds` covers absent, negative, zero, one and maximum ID |

## Guidance actually read

- `AGENTS.md`, `.github/pull_request_template.md`.
- `.github/instructions/`: C# conventions, API endpoints, Blazor architecture, service layer, EF tenancy, validation, testing and observability.
- `.agents/skills/add-api-endpoint/SKILL.md` and route constants, handlers/results, metadata/auth/antiforgery, validation/ProblemDetails references; `add-feature-slice/references/wasm-client.md`.
- `.agents/skills/add-domain-persistence/SKILL.md`, query construction and retrying mutations/locks references.
- `.agents/skills/add-blazor-ui/SKILL.md`, lifecycle/state reference; effective inherited interactivity remains unchanged.
- `.agents/skills/nova-testing/SKILL.md` and applicable harness references, including browser suite. The test author read SQLite and bUnit references and the integration harness.
- Installed `dotnet-test` code-testing-agent, find-untested-sources, test-gap-analysis, assertion-quality and run-tests skills. Broad test research, plan and quality status are kept in non-stageable worktree Git metadata. The run-tests overlay is absent; verified native MTP in `global.json` and the test project/imports.

## Local review and validation

Separate reviewer: `/root/round1_local_review`. Its source review found the remaining trait-choice transport boundary and stale prerender continuation; both were fixed and rechecked. It also strengthened the held-request regression evidence. The [local review record](review-round-1-local-review.md) reports no remaining actionable findings and records the bounded scope, source conclusions and guidance actually read.

Initial validation identified generated migration conventions, a long UI method, test compilation errors, five omitted paging defaults in test expectations and one reversed seeded actor name. These were fixed without suppressing diagnostics or weakening production validation. A test-only derived bUnit component retains a narrow CA1812 exception for reflective construction; no production check was disabled. The narrowed drawer run passed all 103 cases.

Build and format operations were serialized; all tests used `--no-build`, with Aspire-backed suites serialized across the machine. Final results:

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 0 warnings/errors (`round1-build-final.log`). |
| `dotnet format Nova.slnx --verify-no-changes` | Passed, exit 0 (`round1-format-final.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 2,979 passed, 0 failed/skipped (`round1-unit-final.log`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 600 passed, 0 failed/skipped (`round1-integration.log`). The later change strengthened only a unit regression; application/integration source is identical. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 148 discovered: 141 passed, 0 failed, 7 opt-in capture checks skipped (`round1-browser.log`). |
| With `NOVA_A11Y_SCREENSHOTS=1`: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*Captures*'` | 8 passed, 0 failed/skipped (`round1-browser-captures.log`), including all seven omitted checks plus one overlapping case. All 148 distinct browser tests therefore executed successfully across the two runs. |
| `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | No pending model changes (`round1-model.log`); existing tooling/runtime version advisory only. |
| `npm run check:contrast` from `Nova/` | Passed contrast and forbidden Bootstrap-blue checks. |

Raw logs are ignored local artifacts. No checks remain unavailable. Unchanged design captures and measurement decisions remain in the original validation record; this round makes no new visual-fidelity claim.
