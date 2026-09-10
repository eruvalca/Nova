# Local code review — issue #197

Reviewed the complete production change set, new source files, affected unit/integration/browser tests, and the roster handoff documentation against baseline `54c1da3abb16a6d02afbaaf14b59f9cbb870c8ff`. The worktree changed during this review: findings were sent to the implementer as they were verified, and the source corrections observed below are recorded separately from test evidence. Generated comps/scaffolds, binary images and build logs were excluded from code review; the focused test log was read only as validation evidence.

**Current disposition:** All seven actionable code findings are resolved for implementation revision `49e8915bedbc431e1b0304fae2e87538af2a11df`, verified in local Git. The reviewer independently inspected successful build (zero warnings/errors), unit **2,783/2,783**, PostgreSQL integration **580/580**, and final strict browser **135/135** logs, with no skipped tests. The final browser run uses the original five-second assertion allowance, retains exact assertions, and includes the previously failing search, paging, and sort journeys. Final format verification passed with exit code 0 as recorded by the implementer; the reviewer inspected the empty `format-verify.log`, consistent with a clean check. The separate visual gate/user exception remains pending. The sections below preserve the evidence and correction history.

## Findings and disposition

### 1. High — Old roster snapshots were presented under a different query or lifecycle

**Confidence:** Verified by tracing both query navigation and lifecycle refresh.

**Original location:** `Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.cs:878` and `CampaignWorkspace.razor:195`.

```csharp
_rosterError = FirstNonBlank(problem.Detail, "Failed to load the roster. Please retry.");
```

```razor
@if (_roster is not null && !_rosterLoading)
```

**Trigger/impact:** Load an unfiltered Active roster, change discovery, and fail the replacement read. The old `_roster` and `_workingRows` survived while the URL/filter state described the new query. The independent markup branch rendered the old rows underneath the error as if they matched that query. Similarly, an authoritative Active-to-Closed refresh followed by a failed Closed read could display the old Active local outcomes using Closed table semantics. The generation check rejected late responses but did not validate the ownership of previously accepted data.

**Fix:** Associate accepted roster data with authority/campaign/lifecycle/query ownership. Discard mismatched rows and working evidence before a replacement read, retaining previous successful rows only for a retry of that same owner. Cover changed-query failure, lifecycle-change failure, and same-query retry preservation.

**Disposition:** Implementer added `_rosterOwner`, `DiscardUnownedRoster(owner)`, and assignment of the accepted owner. These changes were inspected in source during review. Regression execution and final revision verification remain with the implementer.

### 2. Medium — New regional reads repeated during interactive attachment

**Confidence:** Verified against the effective `InteractiveAuto` composition through `CampaignEntry` and `CampaignWorkspace`.

**Original locations:** `Nova.UI/Features/Campaigns/Components/CampaignWorkspaceReadiness.razor.cs:29` and `CampaignParticipantPlacementContext.razor.cs:34`.

```csharp
var key = $"{Owner}:{CampaignId}:{Status}";
if (!string.Equals(key, _loadedKey, StringComparison.Ordinal))
{
    _loadedKey = key;
    await LoadAsync();
}
```

**Trigger/impact:** A prerendered Active workspace successfully read readiness; the new interactive component instance started with a null key and repeated the request. Closed or off-page participant placement context did the same. The parent persisted its roster but did not persist these child-owned regions. Besides duplicate HTTP work and visible loading churn, a transient attach-time failure replaced a successful regional startup result with unavailable content. This violates the explicit regional startup persistence requirement in the Blazor lifecycle recipe.

**Fix:** Persist successful data or regional errors, initialization, and a matching authority/campaign/lifecycle/participant owner. Restore that snapshot before issuing startup work; preserve explicit regional retries. Exercise restoration with no duplicate service call and rejection of mismatched owners.

**Disposition:** Implementer added owner-checked persistent properties and region persistence to both components. The revised source was inspected. Regression execution remains pending in this review record.

### 3. Medium — A canceled close can leave the surviving drawer without its mobile focus trap

**Confidence:** Possible — the ownership interleaving is visible in code; deferred interop and the actual browser behavior must confirm it.

**Location at review:** `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.cs:642` and its `OnAfterRenderAsync` guard around line 448.

```csharp
_focusTrapInstalled = false;
var module = await _moduleTask.Value;
```

```csharp
if (!_focusTrapInstalled || ParticipantId == _lastRenderedParticipantId)
{
    return;
}
```

**Trigger/impact:** Close participant A in the new desktop nonmodal layout, delay close interop or the parent's `captureScroll`, and select B or restore another participant through history before the close continuation settles. The added owner checks correctly prevent the obsolete close from removing B, but close already marks the trap uninstalled and can already have called the JS teardown. The component is reused for B, and its after-render guard does not reinstall the trap. Resizing to the mobile dialog can therefore leave a surviving drawer without Tab containment or responsive role updates. This is separate from the now-guarded stale `OnClose` callback.

**Fix:** Prefer delivering `OnClose` without preemptive trap teardown; the existing disposal path already tears down the owning dialog and restores focus after successful navigation. Alternatively, give pending close/reopen interop explicit ownership and reinstall the trap for a surviving context. Verify delayed close/capture, new selection, old continuation completion, then mobile Tab containment and proper final teardown.

**Disposition:** Sent to implementer for confirmation and correction. Do not treat the earlier callback ownership guard alone as proof this lifecycle case is closed.

### 4. Medium — New summary component used an inline code block

**Confidence:** Verified.

**Original location:** `Nova.UI/Features/Campaigns/Components/CampaignEffectivePlacementSummary.razor:24`.

```razor
@code {
```

**Problem/fix:** The new component put parameters and label functions in Razor despite the repository's mandatory markup/code-behind separation. Move the existing members unchanged into `CampaignEffectivePlacementSummary.razor.cs` as a partial class.

**Disposition:** Implementer moved the code to a new code-behind file; the final source pair was inspected.

## Other review observations

- Active and Closed identity, counts, filtering, deterministic SQL paging, and page tag enrichment remain within the existing repeatable-read transaction. Closed integrity is evaluated before discovery, including exact participant requests. No schema or mutation-policy changes were found.
- Local outcome/team discovery is separate from effective team and eligibility. WASM validates exact page length against the snapshot count, row identity, local-team evidence, applied-tag uniqueness, numeric order, and portable exact-text ties without imposing a browser text collation.
- The active-only tag choice service remains active-only; archived applied tags are visible and filterable by the read contract but are not discoverable in the normal tag picker. This was not promoted to a new-change defect because active-only choices predate this slice and the direction requires retaining the existing picker capability. The API's archived-tag support must not be described as an expanded archived-tag picker.
- Default `displayName/asc` URL normalization now omits those tokens. Existing workspace assertions at the original lines 1355–1364, 1584–1585 and 1710–1711 still expected explicit tokens when inspected. The implementer was notified to assert preserved default semantics rather than obsolete serialization.

## Evidence and scope

No builds or tests were run by this reviewer, as assigned. The supplied `.impeccable/review/issue-197/focused-unit.log` records 390 tests, 387 passed and 3 failed before subsequent fixes; it is not evidence for the current worktree. Build success and browser work were reported by the implementer and were not substituted for independent runtime evidence. Final build, relevant regressions, all required PR suites, and the tested revision must be recorded by the implementer.

Guidance consulted: `AGENTS.md`; `.github/instructions/` rules for C#, Blazor, API endpoints, services, validation, EF/tenancy, placement decisions, season lifecycle, functional core, testing, UI design and Bootstrap; `PRODUCT.md`, `DESIGN.md`, `.impeccable/surfaces/campaign-spine.md`, `docs/campaign-workspace-roster.md`, and the placement foundation. Recipe sources: the local `code-review` skill with local-review, doctrine and checklist references; `add-feature-slice` with input/validation, service-result and WASM contract references; `add-api-endpoint` and its route/handler/metadata/validation references; `add-blazor-ui` and placement, render-mode, lifecycle/state, binding, forms and JS references; query-construction; and `nova-testing` with SQLite, integration, component and browser references.

The main correctness issue was reuse of an unowned roster snapshot; a targeted source correction has landed. Regional persistence and code-behind corrections also landed during review, while canceled-close trap ownership requires the focused verification described above. This report is a separate local review, not a claim that the unfinished validation gates passed.

## Final continuation review

The implementer requested a second pass after the ownership fixes and the final query optimization. The complete production diff against the same baseline was revisited, including the new people marker, updated readiness presentation, paging/discovery markup, drawer lifecycle behavior, debounce ownership, and the optional filtered root in `EffectivePlacementQueries.WorkingSet`. No production files were edited and no builds or tests were run by this reviewer.

### Earlier finding dispositions

- **Finding 1:** Source correction verified. Accepted roster rows have an owner; query/lifecycle replacement discards mismatched rows before loading. Changed-query and Active-to-Closed failure regressions are present. Same-query regional retry can retain its accepted rows.
- **Finding 2:** Source correction verified in both new regions. Successful or failed startup results and matching owners persist, restoration is checked before a startup read, and explicit retries remain available. Restore and mismatched-owner regression sources were inspected.
- **Finding 3:** Resolved in source. `CampaignParticipantDrawer.CloseAsync` now only invokes `OnClose`; successful removal reaches the existing disposal-owned trap teardown. A rejected parent navigation does not remove the surviving drawer's trap. Updated close/Escape/backdrop tests assert no JS close before disposal and one owning teardown on disposal. The earlier possible case is no longer an open code finding.
- **Finding 4:** Resolved in source; the summary component has its code-behind pair.
- The later accepted-conflict feedback fix is appropriately scoped: `ParticipantOwner` retains accepted feedback across a status-only refresh, while `ContextOwner` still includes status for mutation leases and editable-form invalidation. A participant or authority change clears feedback. `DrawerRetainsConflictAcrossStatusOnlyRefreshAndClearsItWhenContextChanges` covers both identity changes and the read-only transition.

### Query optimization assessment

No correctness or tenant-boundary issue was found in the optimized read. The optional `participantFilter` narrows only the participation side of `WorkingSet`; that helper reapplies tenant, player, campaign, season, Active-status, and current-season checks, while `LatestDecisions` stays unfiltered so an older matching outcome cannot supersede a newer decision. The unfiltered eligibility aggregate still uses the original whole-campaign root. When no effective-team or eligibility filter is supplied, the direct local count is equivalent: the same transaction has already validated the requested campaign's tenant, season and lifecycle, and the left join to the uniquely selected latest decision cannot remove or multiply local participants. Effective-filter counts still use the full working query. The sibling `NeedsPlacement` caller retains the original default root and behavior.

### 5. Medium — Canceled same-query navigation leaves an unapplied search draft visible

**Confidence:** Verified by the continuation and parameter paths; runtime reproduction delegated to the implementer.

**Locations:** `Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.cs:945` and the unchanged-query early return around line 557.

```csharp
if (debounceToken.IsCancellationRequested || ComponentCancellationToken.IsCancellationRequested
    || navigation != _navigationSequence)
{
    return;
}
```

**Trigger/impact:** Type a new search and open/close/select a participant before the debounce settles, or switch a workspace tab without changing the roster query. `CaptureRosterScrollAsync` or parameter reconciliation changes `_navigationSequence`, so the search is discarded. Because the roster query is unchanged, `OnParametersSet` returns before restoring `_searchDraft`. The textbox therefore keeps the abandoned search while rows and URL remain on the old search, until another input happens. The history regression currently changes the actual search query and does not exercise this same-query case.

**Fix:** Either use a discovery-specific generation so participant-only navigation does not abandon a pending search, or restore the abandoned draft to the canonical search when navigation cancels it. A canceled older debounce must never overwrite a newer input draft. Add a participant-only navigation regression asserting textbox, URL and requested search agree after the pending input settles.

**Disposition:** Resolved in source. The debounce checks cancellation first, then restores `_searchDraft` to `_filters.Search` when navigation supersedes the still-current draft. New input cancels the older token before its continuation, so the older continuation cannot clear the newer draft. Reviewed regression sources `CampaignWorkspaceRestoresCanonicalSearchAfterSameQueryNavigationSupersedesDraftAsync` (participant and tab cases) and `CampaignWorkspaceAbandonedSearchCompletionCannotClearNewerInputAfterParticipantNavigationAsync`. Runtime execution remains with the implementer.

### Continuation validation limits

The existing `integration.log` was read and records 580 passed, zero failed, zero skipped before the final optimization. The implementer reported 2,768 unit passes before the latest edits; the current unit log was being rewritten by a new run and was not used as completed evidence. Final build/suite outcomes and tested revision remain the implementer's responsibility. All five local findings are now resolved in source; no additional actionable correctness or security issue was found in the continuation scope.

### Read-only provider performance investigation

The EF query optimization skill was additionally consulted. The prior browser log records the eligibility GROUP BY command at `browser-focused.log:673617` taking 6,064 ms and the effective NeedsPlacement COUNT at line 675135 taking 6,125 ms. Their SQL repeats the saved-decision campaign/player/season/club graph inside the latest-decision anti-join. Command duration alone does not distinguish planning, execution, transport, or concurrent process load.

With the implementer's explicit authorization, the reviewer issued read-only `EXPLAIN (ANALYZE, BUFFERS, TIMING OFF, FORMAT JSON)` against the already-running browser suite's PostgreSQL 18 container. No services, builds, test suites, or data mutations were started. The exact two logged SQL shapes were evaluated with literal non-admin tenant values for an Active campaign containing 60 participants:

| Statement | Planning | Execution | JIT | Shared blocks |
| --- | ---: | ---: | --- | --- |
| Whole-campaign eligibility GROUP BY | 13.914 ms | 2.477 ms | absent | 1,763 hits, 0 reads |
| NeedsPlacement COUNT | 5.562 ms | 0.818 ms | absent | 1,755 hits, 0 reads |

Plans are saved as `eligibility-before-plan.json` and `eligibility-count-before-plan.json`; extracted SQL is in the matching `.sql` files (campaign/club 2). These measurements do not reproduce the six-second command duration or establish a query optimizer/JIT bottleneck. They use a current fixture and literal parameters, so different data shape or parameterized plans remain unproven. No explicit auto-prepare or manual prepare configuration was found in the inspected host/AppHost/service-default/browser paths.

A correlated ordered top-one saved-decision selection could remove the repeated anti-join graph, but would require a provider-specific translation/fallback and new SQL/plan validation. The measured evidence does not justify that extra production change now. Investigate current request/command scheduling and fixture-specific timing before changing the SQL shape or broadening assertion timeouts. The optimized focused browser log independently records 22 total, 16 passed, 5 failed, and 1 skipped; it is not a passing validation gate.

### Browser loading follow-up

The reviewer traced search, page navigation, cross-page participant selection, URL parameter reconciliation, request generations, snapshot projection, and the component disposal base after repeated browser failures showed the correct new URL with a roster loading placeholder. No missing normal completion render was found: `OnParametersSetAsync` awaits `LoadRosterAsync`, and its current response clears `_rosterLoading` before lifecycle completion triggers rendering. Event-owned direct reloads are likewise awaited. The stale-result early return requires a newer roster/detail read or disposal; ordinary navigation does not independently invalidate the roster request generation. The parent reported the same three diagnostic tests passing with unchanged five-second assertions at lower concurrency, while four-way browser runs reproduced loading failures. That report is supporting context, not an independently executed reviewer test.

The proposed narrowly scoped browser synchronization is acceptable with an explicit rationale: after observing the intended new URL, wait up to the existing 15-second readiness budget for the new roster snapshot to settle, then retain the original exact row count, participant name, URL, and error checks. This deliberately changes the allowed completion latency, so it must be recorded as a quality-control change; it does not permit incorrect or unavailable content to pass. Keep the wait restricted to the observed async search and cross-page transitions, and do not increase global timeouts or remove final assertions. For visual capture following a note save, also wait for the refreshed rows and enabled mutation controls: the saved note can appear before the awaited parent roster refresh has settled.

## Final bounded presentation review

Reviewed the latest uncommitted presentation changes against baseline `54c1da3abb16a6d02afbaaf14b59f9cbb870c8ff`: compact `CampaignMenu`, new `CampaignRosterFooter` markup/code-behind/CSS and its parent wiring, the corresponding removal of count/ordering/clear concerns from filters, drawer note/status layout, and targeted browser settlement changes. New files were read in full rather than relying on tracked-file diff output. No production edits, builds, or test runs were performed by this reviewer.

**Disposition: no new actionable findings in this bounded delta.**

- The compact menu preserves the accessible "Campaign menu" name, native button, disclosure state, keyboard handler, admin visibility guard, and existing action callbacks. The workspace supplies the compact button's minimum height and width through scoped descendant styles; the optional parameter defaults to the prior noncompact presentation.
- The new footer is render-only. Parent-owned count, loading, sort, page and filter state pass through unchanged; all ten ordering pairs still call the existing ordering handler, and paging and clear actions retain their existing handlers. The pager receives unavailable counts as zero, which prevents invalid navigation while loading. The selector regression source still covers every ordering pair and reset to page one. Active whole-campaign eligibility counts remain in filters, separate from the footer's filtered result count.
- Note success messages move beside the Notes heading with their live status semantics retained; add/update/delete message literals match the new classification. Tag feedback remains in the general success region. Note/tag permission checks, read-only guards, delete/remove confirmation controls, mutation disabling, ownership checks, and lifecycle reconciliation remain intact. The layout changes do not introduce new persistence or policy behavior.
- The four roster settlement wait sites follow the intended URL change or explicit release of an intercepted pending response. Existing exact filtered count, participant identity, position, URL, empty state and row-count assertions remain. The helper has a local 15-second limit and no suite-wide timeout/concurrency changes. Visual capture now waits for 50 refreshed rows and an enabled Add note control after note persistence.

The reviewer independently read the current `unit.log` (2,775 passed, zero failed/skipped) and `build.log` (success, zero warnings/errors). Contrast success was reported by the implementer. These logs describe the current uncommitted source state; they are not a final tested-commit declaration. **Final browser outcome and tested commit: pending implementer validation.** All earlier five review findings remain source-resolved.

### Unresolved browser evidence after the final bounded review

The current full browser log subsequently reproduced `RosterEmptySearchShowsNoResultsWithZeroCountAnnouncementAsync`: after opening and closing a participant, searching `Nobody McMissing` commits the new URL but leaves `Loading roster...` visible beyond the targeted 15-second wait (test duration 42.604 seconds). This is an unresolved validation failure; the earlier lower-concurrency pass and isolated SQL plans are insufficient to accept the behavior or justify another timeout increase.

A further bounded source trace found no direct drawer cleanup mutation of roster ownership: drawer disposal changes only its own detail/mutation/tag generations and the JS close operation restores focus and removes its trap. Workspace roster generation changes only on a new roster/detail read or workspace disposal. The normal current-result path clears loading and is awaited by parameter lifecycle completion. The remaining evidence must distinguish an incomplete read, an unexpected thrown exception, a superseding request, or an unrendered completion. Current header-only failure output does not distinguish those cases. The earlier SQL measurements did not exercise the search-filtered full row projection; they must not be used to rule out that query shape. Request/server diagnostics and final disposition remain pending with the implementer.

### 6. Medium — Canceled interop escapes workspace and drawer disposal

**Confidence:** Verified for teardown failure; no demonstrated causal link to the empty-search failure.

**Locations:** `Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.cs:677` and `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.cs:668`, with their disposal catch blocks at lines 681 and 678.

**Evidence/trigger:** The completed `browser.log:536883`–536911 records canceled JS interop escaping both disposal routines at 04:02:24, followed by `CircuitHost` reporting an aggregate exception while disposing components. Workspace cleanup was invoking `detachRosterActivationSuppression`; drawer cleanup was awaiting its previously started lazy module import. Further disposal failures appear at 04:02:57 and 04:03:04. Both routines catch `JSDisconnectedException` only, although the actual shutdown result was `TaskCanceledException` (an `OperationCanceledException`). The stack is renderer-wide disposal, which may follow browser/test-context teardown; it must not be presented as proof of the search spinner's cause.

**Smallest fix:** In these two disposal-only try/catch blocks, additionally contain `OperationCanceledException` when `ComponentCancellationToken.IsCancellationRequested`. `NovaComponentBase.DisposeAsync` marks the component disposed and cancels its lifetime before calling `DisposeAsyncCore`, making that check an explicit disposal boundary. Preserve the existing disconnect handling and let unrelated JS exceptions remain observable. Do not pass the already-canceled component lifetime token into ordinary cleanup calls: that would skip trap/listener teardown on healthy navigation. Verify canceled import and canceled cleanup interop do not escape disposal, while successful cleanup still executes once.

**Sibling inspection:** `CampaignEntry`, `NewCampaign`, and `ClubShell` also only catch disconnect around module disposal; the cropper adapter has a similar pre-existing disconnect-only guard. None appears in these logged failures. This is not justification for broad exception suppression or a shared base-class catch; the proposed immediate correction is limited to the two observed workspace/drawer cleanup paths. Non-JS sibling disposal in `TeamDetail` only cancels/disposes its scoped source and is not affected by this interop path.

**Disposition:** Sent to implementer for the narrow correction and regression validation. The full browser log independently records 135 total, 133 passed, two failed, zero skipped. The empty-search and placement-navigation failures remain unresolved independently of this teardown finding; final tested commit remains pending.

## Provider investigation during the next full browser run

The implementer authorized read-only inspection of the next suite's existing PostgreSQL 18 container. The reviewer changed no database settings, schema, indexes, data, production code, or timeout limits and ran no builds/tests. Existing container credentials were consumed without printing them. The previously recommended disposal cancellation catches were verified in both production cleanup blocks; regression source now covers cancellation and unrelated-JS-error behavior, but the implementer reported two pending-import test-harness failures that still need correction.

### 7. High — The working-set latest-decision anti-join can multiply a 60-player roster into 216,000 inner scans

**Confidence:** Verified execution-plan defect; its contribution to particular browser failures requires the implementer's final run correlation.

**Location:** `Nova/Features/Campaigns/EffectivePlacementQueries.cs`, the `WorkingSet` left join to `LatestDecisions` (around lines 43–46) and the latter's correlated anti-join (around lines 21–25).

At approximately 04:20 UTC in the diagnostic full run, the exact aggregate SQL for Active campaign 3 / club 2 / 60 participants reproduced 4,749.208 ms execution with only 25.986 ms planning. JIT was absent. The plan estimated total cost 121.48 and one row at successive joins, but read 922,092 shared buffer hits (zero disk reads). Its outer left join applied `p.PlayerId = p2.PlayerId` only as a join filter after running the full latest-decision anti-join once per local player. That anti-join likewise applied candidate/newer player correlation after enumerating all saved players. Consequently the newer-decision assignment and season scans ran **216,000 times (60 × 60 × 60)**. See `eligibility-diagnostic-early-plan.json`.

Later the same literal query on the same campaign executed in 3.538 ms after table auto-analysis had occurred. The defect therefore depends on estimates and join order, not simply monotonic table growth. At 04:23:03, `pg_stat_activity` showed 12 concurrent active aggregate/count queries aged 5.20–11.45 seconds with no wait event; the observation and contemporaneous table statistics are saved in `provider-late-observation.txt`. Prior full-run logs also contain aggregates lasting 12.139–12.327 seconds around 04:00:59. Earlier isolated fast plans did not cover this bad plan choice and must not be treated as evidence of stable performance.

**Read-only correction prototypes:**

| SQL shape | Planning | Execution | Largest relevant probe loops | Evidence |
| --- | ---: | ---: | ---: | --- |
| Original, bad selected plan | 25.986 ms | 4,749.208 ms | 216,000 | `eligibility-diagnostic-early-plan.json` |
| Per-local correlated LATERAL ordered top one | 10.807 ms | 2.306 ms | bounded per player | `eligibility-lateral-proposal-plan.json` |
| Per-local nullable scalar latest ID, then saved-decision left join | 13.171 ms | 3.994 ms | 120 | `eligibility-scalar-proposal-plan.json` |
| Scalar-ID shape, NeedsPlacement count | 9.848 ms | 2.963 ms | bounded per player | `eligibility-count-scalar-proposal-plan.json` |

All prototypes preserved the exact existing saved-decision tenant, related-player/campaign/season, current-season, lifecycle, non-null opening sequence, and non-undecided filters. The scalar aggregate used 2,194 shared hits and no JIT. The original and scalar aggregate executed inside one SELECT produced the same row (`Resolved`, count 60) for the inspected campaign. These observations establish the SQL direction; they are not a substitute for the existing mixed-decision/tenant/lifecycle regression matrix.

**Recommended bounded EF correction:** Factor the existing saved predicate without changing it. For each local participation, select a nullable assignment ID from saved decisions for that player ordered by season opening sequence descending, then assignment ID descending, with `FirstOrDefault`. Left-join the saved entity by that ID, then retain the existing validity, eligibility and correction projection once. An empty saved sequence must yield null, preserving the local row without a decision. This selects the same deterministic maximum pair as the anti-join, before applying team/player validity. Since both local and saved roots enforce the same club's current season, no historical season can enter the selection. Local discovery continues to narrow only the participation root; saved decisions must remain unfiltered by discovery or validity.

This scalar-ID construction is preferable to introducing LATERAL/APPLY provider branches: it can be represented by a correlated scalar `SELECT ... ORDER BY ... LIMIT 1` on both SQLite and PostgreSQL. **Actual EF translation remains to be verified on both providers.** Keep `LatestDecisions` sibling callers unchanged except for factoring the identical saved predicate, and ensure `NeedsPlacement`, which calls `WorkingSet`, receives the same corrected projection. Do not disable JIT, force planner settings, add an index/migration, or extend timeouts to address this demonstrated repeated-work shape.

**Disposition:** Evidence and recommendation sent to implementer. Production correction, generated-SQL verification, regression results, final browser outcome and tested commit remain pending.

**Source correction review:** The implementer subsequently added `SavedDecisions` by factoring the exact former predicate, left `LatestDecisions`' existing anti-join behavior intact for its external consumers, and changed `WorkingSet` to the nullable per-local ordered ID plus left join back to saved. The source was inspected: null preserves no-decision rows; the total ordering chooses the original winner; the one shared validity/eligibility/correction projection is unchanged; tenant and current-season guards remain on both roots. `NeedsPlacement` continues to consume the corrected working query. No source-equivalence defect found. `git diff --check` for this provider file passed. Generated EF SQL and all provider/regression/browser validation remain pending; the SQL prototype's successful plan does not establish EF translation success.

### Compiled-provider and cleanup validation update

The reviewer independently read the subsequent completed logs: build succeeded with zero warnings/errors; unit tests passed **2,783/2,783**, zero skipped; PostgreSQL integration tests passed **580/580**, zero skipped. The pending-import harness cases were corrected and are included in that full unit result. Finding 6 is now source-resolved with passing regression evidence.

During the next browser run, the reviewer inspected only the existing DCP stdout file, without database workload or EXPLAIN. **Actual compiled EF translation is confirmed:** the working eligibility aggregate emitted at 04:35:32.971 contains a per-local correlated assignment-ID subquery ordered by season opening sequence descending and assignment ID descending, limited to one, equated to the left-joined saved assignment ID. That working aggregate contains no `NOT EXISTS`. Its full SQL is saved in `provider-actual-ef-aggregate.sql`. The campaign-list sibling NeedsPlacement count also emitted the scalar shape; see `provider-actual-ef-scalar-excerpt.log`.

Those early-run commands logged 104 ms for the working aggregate and 13 ms for the campaign-list query. They establish the intended generated shape, not a measured execution plan or a full-run latency guarantee. The earlier prototype plans remain explicitly separate evidence. Finding 7 is source-resolved, with actual PostgreSQL translation observed and both unit/SQLite and PostgreSQL integration behavior suites passing. **Final browser outcome, format verification, visual-gate disposition, and tested commit remain pending.**

### Restore strict browser completion allowance

With the concrete query defect corrected, the reviewer endorses removing the diagnostic 15-second timeout override from `WaitForRosterSettlementAsync` and the capture-specific row-count wait. Retain the explicit transition synchronization, exact result assertions, and Add note enabled check, using Playwright's original default five-second allowance. The earlier 15-second rationale was provisional diagnostic synchronization; it is not justification for retaining a relaxed shipping gate after correcting the underlying repeated work. A passing rerun with the restored strict helpers is required; the currently running 15-second-helper suite, even if successful, remains interim evidence. Source change and strict rerun disposition are pending the implementer.

### Sort-transition diagnostic follow-up

The interim browser run subsequently reported `UrlStateSurvivesReloadAndBackForwardRestoresDrawerAsync` timing out while waiting for `sortBy` after the filtered one-player result was already displayed and the Name header click returned. A bounded source review found no demonstrated click suppression: the header forwards one native button click to the existing callback; default ascending toggles to descending; the changed-URL branch reaches `NavigateTo` synchronously. Keyboard activation suppression is restricted to Enter/Space on row/card targets, and scroll/focus helpers do not navigate. The default-name first-click unit regression is present and passed in the full unit run. No matching unhandled/circuit error was found in the current server stdout at inspection time.

This remains unresolved browser evidence, not a confirmed new source finding. The next discriminating capture should record one pointer/click sequence, its target and prevented state, before/after `aria-sort`, all URL transitions, and console/page errors. If a DOM click is proven but no navigation occurs, a temporary handler-entry/target-URL diagnostic can locate the missing boundary. Blind retries of a toggle can reverse an already-applied sort and should not substitute for this evidence. Final strict browser and visual/format/commit gates remain open.

The implementer reported the focused sort journey passing without reproduction. The reviewed shipping helper now asserts ascending before the single click and descending after it, retains the URL assertion, and records pointerdown/pointerup/click targets plus URLs if failure occurs. It performs no retry or timeout extension. Its capture-phase `defaultPrevented` field describes early dispatch only, not whether a later listener prevents the event. Both roster settlement and capture row-count waits now use the original default timeout. A stale comment describing 15 seconds was reported for correction; the code itself contains no such override. Final strict-suite result is pending the implementer.

## Final strict-suite disposition

The reviewer independently read the final `browser.log`: **135 passed, zero failed, zero skipped**, duration 5m 01.670s. The helper uses Playwright's original five-second assertions, and the stale timeout comment has been corrected. The single-click sort helper retains its stronger ascending/descending and URL checks; its full journey now passes together with the other browser workflows. The earlier search, cross-page and sort failures are no longer outstanding validation failures on this source state. Findings 1–7 are closed with the source, generated SQL, regression and full-suite evidence above. No further actionable code finding remains.

The validated implementation is committed locally as `49e8915bedbc431e1b0304fae2e87538af2a11df` (`Implement campaign workspace and roster discovery for #197`). The reviewer verified that commit and inspected the final format log. The full suites ran before this commit against the same production and test behavior; the implementer records only UTF-8 BOM corrections in `CampaignEvaluationBrowserTests.cs` and `PendingModuleRuntime.cs`, plus the explanatory browser-helper comment, after those tests. Final `dotnet format Nova.slnx --verify-no-changes` completed with exit code 0 according to the implementer's execution result and validation record; its inspected log contains no diagnostics. Subsequent review/validation-record updates are evidence-only. The visual-gate disposition or explicit user exception remains pending and is not approved by this code review.
