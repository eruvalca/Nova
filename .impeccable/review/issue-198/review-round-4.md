# Review round 4 — PR #253

Base: `339ee66e3e3fdd13e67dd74a28795f22c30bdb05`. Both CI checks succeeded. Completed Copilot review `5174200087` (session `71c6d74e-63d5-4cfa-b69c-470a99ca0201`) posted one comment, but its raw stored log contains two findings. Both are included in this round; withholding is not treated as resolution. Review bodies, paginated threads and issue comments were inspected through GitHub CLI. No review was requested.

| Finding | Disposition |
| --- | --- |
| Withheld: revoked or aborted history traversal reports success and strands the component bypass flag | Shared JS now requires both owned, unrevoked acceptance and commitment. Input/pending changes, cancellation and detach settle revocation independently. Both C# bypass flags are removed so a fresh owned protection callback remains accepted while an older replay completes. |
| Posted `PRRT_kwDOSz2VcM6hUBoW` / `3985237898`: three evidence endpoint names are inline literals | Moved names into `CampaignEndpoints`, preserving their exact values. Sibling inspection corrected the three effective-placement names through their owning campaign/season constants. No route, authorization, handler or response changed. |

Independent local review additionally identified stale C# departure continuations after module/release awaits, released browser protection left behind by an abandoned departure, and duplicate Evaluate discard during pending storage. Both callers now capture request/owner context, recheck it after awaits, retain owned interrupted/error prompts, revoke abandoned releases, and serialize each departure through its full asynchronous lifetime. Pending mutation payloads remain independent and unchanged.

The guard handles committed-before-popstate and popstate-before-committed ordering, current-entry no-op, duplicate replay, rejected/synchronously throwing traversal, and a rejected enhanced-navigation finished promise after accepted commitment. The controlled browser protocol scenarios import the actual shipped module on an isolated same-origin page; they control Navigation API ordering, and do not claim to replace the existing native browser history/link flows.

## Guidance and validation

Read/reused `AGENTS.md`; C#, Blazor architecture, API, validation and testing instructions; `add-blazor-ui` lifecycle/state and JS-interop recipes; `add-api-endpoint` route constants, handlers/results, metadata/auth and validation references; `nova-testing` component and browser references; test-generation and test-running skills. The reviewer and test author retain their own read-source records. Existing ownership/navigation guidance already states the corrected invariant; no additional permanent instructions or new skills are needed for this round.

Validation and the final source fingerprint will be recorded before the single combined commit. Build runs before `--no-build` tests, Aspire suites run serially across the machine, and application source/assets remain fixed throughout browser validation. JavaScript syntax checks passed for the shared guard and both collocated adapters. No diagnostic was suppressed or check weakened.

| Requirement | Regression evidence |
| --- | --- |
| Interrupted replay accepts the latest owned prompt and can retry | `EvaluationInterruptedHistoryDepartureAcceptsNewestPromptAndRetryAsync`, `DrawerInterruptedHistoryDepartureAcceptsNewestPromptAndRetryAsync` (false/JS exception; callback before/after completion) |
| Old release cannot navigate or overwrite a replacement owner | `EvaluationOldReleaseCompletionCannotNavigateOrOverwriteNewOwnerAsync`, `DrawerOldReleaseCompletionCannotNavigateOrOverwriteNewOwnerAsync` (success/JS exception) |
| New draft survives a delayed release and restores browser protection | `EvaluationNewDraftDuringReleaseRevokesDepartureWithoutLosingDraftAsync`, `DrawerNewDraftDuringReleaseRevokesDepartureWithoutLosingDraftAsync` |
| Discard is serialized through pending draft persistence | `EvaluationDuplicateDiscardDuringPendingPersistenceIssuesOneDepartureAsync` |
| Browser commitment, acceptance, revocation and cleanup ordering | `HistoryReplayRequiresOwnedUnrevokedAcceptanceAsync` (14 controlled cases, including canceled release and foreign-owner rejection) |

Initial builds reported missing braces, an async field-flow diagnostic, method length, a missing test import and explicit ordinal-comparison requirements. These were corrected through braces, the existing broader mutation-blocking capability, extraction of unchanged owned cleanup, the import and ordinal comparisons. No diagnostics were suppressed. Final `dotnet build Nova.slnx` passed with 0 warnings/errors (`round4-build-complete.log`, 13.31s). `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` passed 3,020 tests, 0 failed/skipped (`round4-unit.log`, 18.058s), including all 15 new cases.

`dotnet format Nova.slnx --verify-no-changes` exited 0 (`round4-format-verify.log`). The full integration suite passed 600/600 with 0 failures/skips (`round4-integration.log`, 1m47.365s). No theme or persistence schema changed; contrast and migration-model checks at `af999242` remain applicable. The browser suite runs separately after integration, with a temporary process-scoped keep-awake request cleared in `finally`; no persistent power setting is changed.

## Final validation

The tested application/test patch against `339ee66e3e3fdd13e67dd74a28795f22c30bdb05` has SHA-256 `F181E061C53EBD7167FA97E4BC31CE23F96351FB1F96C2C1E517B578366DA98C` (`git diff --cached --binary` over `Nova`, `Nova.UI`, `Nova.Client`, `Nova.SharedKernel` and all three test projects). Only Markdown evidence was finalized after validation; the single combined commit contains this source.

| Command | Final result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 0 warnings/errors (`round4-build-complete.log`). |
| `dotnet format Nova.slnx --verify-no-changes` | Exit 0 (`round4-format-verify.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,020 passed, 0 failed/skipped (`round4-unit.log`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 600 passed, 0 failed/skipped (`round4-integration.log`). |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 162 passed, 0 failed/skipped, 3m33.214s (`round4-browser.log`), `NOVA_A11Y_SCREENSHOTS=1`. |
| `node --check` shared guard and both collocated adapters | Passed. |

The full browser suite includes all 14 new controlled protocol cases and existing native links, modified clicks and Back/Forward flows. All source/assets stayed fixed during the run. Temporary keep-awake requests were released on completion. The [independent review](review-round-4-local-review.md) records no remaining actionable source/test findings and the inspected command evidence. No check is unavailable. Fresh CI and automatic review remain required after the push; no merge or manual review request is authorized.
