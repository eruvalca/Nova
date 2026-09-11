# Review round 7 — capability gates and protected trait drafts

## Scope and collected evidence

The user explicitly resumed continuous PR watching after the retention cleanup.
Base: `f76ae55738145587cec09b272366c7441ea59901`, 271 changed PR files. CI Build and
Unit Tests both passed. Automatic review `5179984193` is COMMENTED, recommends
changes, and reports five moderate findings and two posted comments; it is not an
approval or the clean human-review stopping exception.

Read every paginated review and issue comment, all review threads and their comments
(no further thread or nested-comment page), and session
`aba9db3c-f9df-4d8a-9313-2e9f3ea03d41`. Its raw log exposes four complete stored
comments from one ensemble member. Actions workflow `34610031334` contains eleven
stored entries, including repeats, covering six distinct source areas. Its final
classifier reports five findings; the complete grouping/full wording of the remaining
ensemble entries is not exposed. All located areas were assessed independently rather
than treating suppression, duplicate location or missing wording as resolution.

## Dispositions

| Source area | Disposition |
| --- | --- |
| Evaluate `razor.cs:75–76`, shared note capability gate | Use independent add-note, apply-tag and placement capabilities. Edit/delete and trait removal combine current Active identity with their row-specific capability. Recheck at invocation as well as rendering; preserve server-authorized receipt recovery independently of new-mutation gates. |
| Evaluate `razor.cs:78–79`, trait-label draft | Persist and restore the trait search/label, include it in draft protection and discard, keep it copyable when capture becomes unavailable, and clear only the matching label/filter on successful create/apply. Note settlement retains unrelated trait text. Native navigation also protects dirty trait input before the .NET render catches up. |
| Evaluate `Interop.cs:24`, posted thread `PRRT_kwDOSz2VcM6hg5AV` | Inapplicable suggestion to add role to the storage key. UI ownership/capabilities are invalidated by authority changes; the stable user/club/campaign/participant key preserves the original unresolved operation. The server reauthorizes current membership before receipt recovery and rechecks mutation permissions. Changing the key would strand the original ID and risk duplicate work. Clarified both the call site and shared recovery instructions. |
| Drawer `Recovery.cs:28`, posted thread `PRRT_kwDOSz2VcM6hg5A4` | Same stable-storage/current-authority distinction. A committed receipt is immutable evidence, not permission to perform a new write. Uncommitted administrator-only removal still checks current role. Clarified the call site; no storage-key behavior change. |
| Evaluate `razor.cs:165–175`, owner reset | Clear the old identity error, regional loading state and trait-removal confirmation before a new player loads. Reset picker/search as part of the new owner's state, then restore only that owner's retained capture. Drawer sibling already clears its detail error and mutation UI state. |
| Evaluate `razor.js:31–38` (also `31–32`), stored-capture read | No additional defect established from the unavailable wording: malformed JSON/shape leaves original bytes untouched and protection enabled; C# validates operation-specific payload/assignment before readiness. Extend the existing strict shape check for the newly persisted trait field. Do not invent a finding or relax validation. |

## Guidance and review

Read the current root AGENTS, matching C#/Blazor/validation/testing/UI rules and
`add-blazor-ui` with lifecycle/state/recovery and JS interop references; the prior
service/API/tenancy review sources remain applicable to the unchanged server boundary.
The testing agent applies `nova-testing` and its component/browser references and
the installed .NET test pipeline. The separate reviewer records its own sources in
[review-round-7-local-review.md](review-round-7-local-review.md).

The shared Blazor instruction previously said recovery context includes permission
changes without distinguishing UI authority from durable operation identity. It now
states that distinction explicitly. This single path-scoped file is read by both
ecosystems; no duplicate instruction or skill was introduced. Tests, not suppression
or weakened checks, establish the corrected behavior.

## Validation

The first build failed with two CS1061 errors in new reload tests: the rendered
bUnit wrapper has no `DisposeAsync`. Both now use the existing sibling `Dispose`
pattern. No production compile failure was reported. The subsequent full solution
build passed with zero warnings/errors in 1m 49.26s (`round7-build-final.log`).

`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` passed:
3,126 succeeded, zero failed/skipped, 1m 37.196s (`round7-unit.log`). The batch adds
27 unit rows and four browser rows. Both changed JavaScript modules pass
`node --check`. Browser and full format validation remain pending.

Test source is base `f76ae557` plus the 15 application/test files listed in the
local `round7-source-manifest.log`, SHA-256
`5804975F0F8943A9693CE18C760EEE576A4CF01C014666AF9BD4148FEE4C63E2`.
The committed round-seven source identifies these changes durably; subsequent
validation-record edits do not alter the tested application. Earlier round-six
full-browser failures remain historical limitations, not passing evidence for this
round. No commit, push or thread resolution has occurred for round seven yet.

### Browser failure assessment and corrective validation

The initial full browser run completed, rather than being interrupted: 176 total,
171 passed, five failed, zero skipped, 10m 57.110s (`round7-browser.log`). It used
`--no-build --output Detailed --long-running 90 --xunit-diagnostics on` with
`NOVA_A11Y_SCREENSHOTS=1`. All newly added storage/navigation cases passed.

- Roster page-boundary navigation: the second full navigation discarded the earlier
  attachment proof. The subsequent predicate waited up to 30 seconds for a position
  element intentionally absent during loading. Prove Add note → Cancel on that same
  document, send Next once, and observe the exact position without waiting for a
  missing element. Preserve the exact 51-of-60, selected identity and URL assertions.
  The old evidence proves the event executed, not when the owner changed; no
  production cancellation defect is inferred.
- Lost-response recovery: both initial and post-reload composer attachment now use
  the existing responsive scenario's handled-input proof, extracted unchanged into
  `EvaluationInteractionHelpers`. This explicitly broadens functional startup from
  a bare five-second Save assertion to the existing bounded hydration policy. It
  preserves surfaced-error failure, real binding, cleanup and Save readiness, with
  the reload probe inside the server-negotiation observation. There is no specified
  five-second startup SLO; no retry constants or mutation assertions changed.
- Two club-crest creation scenarios: fill bound text only after a successful crop
  save proves attachment, then blur the final field. Earlier fills could occur in
  prerender while only the later upload was retried. The component has no field
  reset on crop save. All creation/navigation/aspect assertions remain unchanged.
  The separate reviewer implemented this small test correction; the primary agent
  independently reviewed its actual diff.
- Closed archived roster: explicit reload timed out waiting for document load;
  the diagnostic page evaluation also failed, leaving only its URL. No justified
  correction was established. The test and its assertions remain unchanged.

The final solution rebuild passed with zero warnings/errors in 41.71s
(`round7-push-build.log`). The 20-file application/test push manifest has SHA-256
`A5282E8934631A6A197F5420AC132C1C363CB2648ABCB48296EE254CA6D0F7D1`
(`round7-push-source-manifest.log`). Application/unit source remains byte-identical
to the passing unit run. A complete browser rerun and format verification are pending.

The second complete browser run finished with 176 total, 175 passed, one failed,
zero skipped in 4m 59.060s (`round7-browser-final.log`). All five previously failed
cases passed, including the unchanged Closed reload. The remaining failure was
`NoteFlowIsKeyboardOperableWithLabelsAndStatusAnnouncementsAsync`, **before**
composition: Close receives initial focus before recovery enables Add note, so an
early focus call on the disabled control can do nothing. Its setup now uses the
existing bounded policy to focus the current enabled button; the explicit focus,
Enter activation, typed content, Save Enter and status assertions are unchanged.
Separate review verified the actual lifecycle and final correction.

After that final test-only correction, `dotnet build Nova.slnx` passed with zero
warnings/errors in 11.72s (`round7-keyboard-build.log`). The full changed class,
`--filter-class '*CampaignEvaluationBrowserTests'`, passed all 20 tests with zero
failed/skipped in 1m 41.375s (`round7-browser-focused.log`), with accessibility checks
enabled. This focused pass does not relabel either full run as passing. A complete
clean browser run and all three local suites remain required before merge.

Final 20-file application/test manifest SHA-256:
`CC381131C0DE90AFFDD8D0186E432F89662AD38356EC63D1A6E82159B937490B`
(`round7-final-source-manifest.log`). The unchanged server/provider boundary retains
the 605-test integration result recorded at `f76ae557`; no new integration run is
claimed for this UI/test-only round. Format verification remains pending.

Final `dotnet format Nova.slnx --verify-no-changes` passed, exit 0 with no diagnostics
(`round7-format-final.log`). The first format pass reported one whitespace issue
and missing UTF-8 BOMs in the three new C# files; these were corrected. The only
post-execution source changes are that line break in
`CampaignEvaluationPanelTests.Capabilities.cs` and BOMs in it, `TraitDraft.cs` and
`EvaluationInteractionHelpers.cs`. No executable behavior changed. Final commit
source manifest SHA-256:
`6B0F111E38E43E57391E5081D529FCF7B2D2EA8F62BEF76E96C328E17362E44B`.
The separate reviewer verified all 20 pre-format hashes and execution logs; the
primary agent checked these final mechanical changes. Whitespace diff checks pass.
This complete round is delivered as one combined commit; fresh CI and automatic
review follow, with no manually requested review or merge.
