# Review round 5 — PR #253

Base: `33a07928fedac5aa86ff3586580b7258f4b49ff8`. Both CI checks succeeded and all 15 previous posted threads were explained/resolved. Copilot review `5174428186`, session `9d1bfeda-c3e5-44c0-ad0e-4c29e622cfee`, reports **six unresolved moderate findings** despite posting zero comments. This is not the user's clean “Needs a closer look” stopping exception.

## Finding discovery and dispositions

The GitHub CLI raw session log and `/events` expose one ensemble member and only the tag-cap finding's full text. The Actions log for run `34553820687` identifies all six stored findings by type, path and original line; its severity classifier confirms six retained candidates. No artifacts or additional posted review comments were returned. The other five full descriptions are unavailable through these responses. The independent assessment below uses the located source and reproducible behavior, without inventing hidden comment text or treating withholding as resolution.

| Stored finding location at the base | Disposition |
| --- | --- |
| `CampaignEvaluationPanel.razor.cs:185`, bug | Import failures now stay within owned recovery initialization, expose explicit retry, and release only the corresponding failed cached load. The drawer's failed import/factory and initial open paths receive the same treatment. |
| `CampaignEvaluationPanel.razor.cs:194–195`, bug | Attachment markers publish only after successful current-owner attachment. Retry reinstalls required protection before storage becomes ready; failed renders do not loop automatically. |
| `CampaignEvaluationPanel.razor.js:31–34`, bug | Missing/falsy pending values and malformed retained bytes reject without rewriting storage. C# validates the restored snapshot, recognized operation, assignment ownership and shared input constraints before publishing capture state. |
| `CampaignParticipantDrawer.Recovery.cs:49`, bug | Validate the nonnull envelope, recognized operation and typed payload using existing input contracts before publishing state. Restore/replay share the validated parser; invalid retained bytes remain untouched and cannot dispatch. |
| `CampaignWorkspace.razor.cs:593`, codebase convention | No source change: stable authenticated user/club capture scope intentionally survives administrator-role changes. Separate authority ownership includes role and invalidates capabilities; both children append campaign/participant and the server reauthorizes replay. Removing that distinction would strand retained operations. Independent inspection found no applicable convention violated by this construction; this disposition is limited to the located source, since full comment text was not exposed. |
| `CampaignTagApplicationService.cs:98–100`, maintainability | Use `TagDefinitionLimits.MaxActiveTagDefinitions` for count and message. Sibling HTTP tag-choice validation now uses the same constant; create/restore already did. The cap remains 100. |

Sibling and local-review corrections preserve the same-capture JavaScript revision watermark and pending protection across finder reattachment, serialize complete storage retries, reject stale restore completions, and keep newer draft text separate from pending payloads. Optional focus/scroll failure does not invalidate mandatory protection. Ambiguous HTTP outcomes retain their existing replay-ready storage state.

The same workflow cross-check was applied to all five older review sessions. Rounds one through three match their recorded stored findings. Round four's workflow `34551303142` reveals two additional locations absent from its raw session stream; both are included in this combined round rather than ignored:

| Additional location at `339ee66e` | Disposition |
| --- | --- |
| `CampaignEvaluationPanel.razor:93`, error message | Capture unavailability on an Active campaign no longer claims the campaign is read-only. The Closed explanation is reserved for authoritative Closed state. Active/Closed component cases verify the distinction. |
| `CampaignEndpoints.cs:18–20`, codebase convention | Expression-bodied forwarding is supported by the recipe and needs no change. Independent inspection found a narrower builder contract gap: invalid supplied cursor values could be emitted. The shared helper now omits invalid/partial cursor pairs for both histories; direct builder tests check boundaries and offset escaping. Existing HTTP input validation remains unchanged and already prevented dispatch of invalid input. |

As with the five current non-cap locations, the older findings' full wording was not exposed; their type/path/line and source behavior are recorded without claiming access to unavailable text.

## Guidance and validation

Read/reused `AGENTS.md`; C#, Blazor architecture, service, validation, API, tenancy and testing instructions; `add-feature-slice`, `add-blazor-ui` lifecycle/state and JS-interop references; `nova-testing` component and browser-suite references; test-generation and run-tests skills; and the PR template. [Independent review](review-round-5-local-review.md) records source-based findings and corrective-diff inspection. Existing guidance already covers these invariants; no new skill or duplicate permanent rule is justified.

Initial builds exposed method length and test-helper naming, nullability and disposal diagnostics. These were corrected through a cohesive helper, null-safe checks, conventional naming and DI-owned test fakes, without suppressing those diagnostics. Two narrowly scoped CA1812 exceptions on bUnit's reflectively constructed lifecycle observers are explained and independently reviewed. The first targeted run failed 11 drawer recovery cases because `OnAfterRenderAsync` failure state was not rendered. The owned restore catch now requests that render; all 225 targeted cases passed. A subsequent full unit run passed 3,058 cases before the final URL-builder correction. The first URL run failed five data rows because integer attributes did not bind to nullable long parameters; explicit long literals corrected the test data. The new URL file's required UTF-8 BOM was also corrected. These intermediate logs remain retained; no test was skipped or weakened.

Build precedes tests, tests use `--no-build`, Aspire suites remain serial across the machine, and source/assets stay fixed during browser execution. No check is weakened. One combined commit/push will contain the complete round, followed by fresh CI and automatic review without requesting one.

## Final validation

The application/test patch against `33a07928fedac5aa86ff3586580b7258f4b49ff8` has SHA-256 `D5D34FCAB17F48A1060DA146772D5F3C168A4A1EF4FC9431F60BC8822037DDFE` (`git diff --cached --binary` over all application/test projects, saved directly with `--output`). Only Markdown evidence is finalized after execution.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 0 warnings/errors, 9.82s (`round5-build-tested.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,066 passed, 0 failed/skipped, 18.898s (`round5-unit-tested.log`). |
| `dotnet format Nova.slnx --verify-no-changes` | Exit 0 (`round5-format-tested.log`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 600 passed, 0 failed/skipped, 1m43.961s (`round5-integration.log`). |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 172 passed, 0 failed/skipped, 3m36.626s (`round5-browser.log`), with `NOVA_A11Y_SCREENSHOTS=1`. |
| `node --check` both collocated adapters | Passed. |

| Requirement | Exact regression evidence |
| --- | --- |
| Import/attachment/storage failures expose usable retry without automatic loops | `EvaluationRetriesFailedInteropInitializationBeforeSavingAsync`, `DrawerRetriesFailedInteropInitializationBeforeSavingAsync` |
| Malformed retained state remains undispatched; repaired state replays the original operation | `EvaluationRejectsMalformedSnapshotThenReplaysValidOriginalOperationAsync`, `DrawerRejectsMalformedStoredOperationThenReplaysValidOriginalAsync` |
| Old-owner completion cannot affect the new owner | `EvaluationIgnoresOldOwnerInteropCompletionAfterNewOwnerRestoresAsync`, `DrawerIgnoresOldOwnerInteropCompletionAfterNewOwnerRestoresAsync` |
| Duplicate storage retry does not issue overlapping reads | `EvaluationDuplicateStorageRetrySharesPendingReadAndRecoversOriginalOperationAsync`, `DrawerDuplicateStorageRetrySharesPendingReadAndRecoversOriginalOperationAsync` |
| Copy distinguishes Closed from unavailable capture | `EvaluationUnavailableCaptureDoesNotMisreportActiveCampaignAsReadOnly` |
| Both history builders omit invalid cursors and preserve valid offset encoding | `HistoryBuildersOmitTheWholeInvalidOrIncompleteCursor`, `HistoryBuildersPreserveTheExclusiveCursorAndEscapeItsOffset` |
| Browser validation preserves original bytes and pending protection | `InvalidRetainedCaptureStaysProtectedAndPreservesOriginalBytesAsync` |
| Finder reattachment cannot accept an older write or drop pending protection | `FinderReattachmentPreservesPendingStateAndRejectsOlderWritesAsync` |

The new cases comprise 38 component, eight direct URL and ten browser-module cases. Existing cap coverage (`AtActiveDefinitionCapExistingTraitsRemainApplicableAndNewNamesAreRejectedAsync`) verifies existing traits remain applicable at 100 while new names reject. The defensive drawer draft tuple is source-reviewed; its editor is readonly while restoration is unavailable, so no artificial private-state test claims a reachable typing race. No theme or schema changed; prior contrast/migration-model results remain applicable.

All three suites passed on this source. The browser run includes the existing server/WASM, native history/link, capture, ownership, lifecycle and accessibility scenarios plus the ten new storage cases. Application/test source and generated assets remained fixed during execution. Integration/browser runs used temporary process-scoped keep-awake requests, released in `finally`; no persistent power setting changed. There are no unavailable local checks. Fresh CI and automatic review remain required after the single push; no review request or merge is authorized by this record.
