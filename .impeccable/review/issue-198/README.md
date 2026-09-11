# Evaluation workspace implementation record (#198)

## Scope and revision

Branch `codex/issue-198-evaluation` starts at `3e0253c28677838858b1887b2351a133eac7bc16` (merged #252). The surface implements the confirmed find → verify → capture → move-on workflow. Parent campaign-loop acceptance stays with #170; Place #199 and Close #200 redesigns remain separate.

Original implementation production and test source was validated at `381d501950d064d426ddce7c772fe449c485cfde`: build, format, all three local suites, contrast, JS syntax and migration-model checks passed. Subsequent review fixes and their tested revisions are recorded in the review-round records linked below; this original evidence does not replace those later checks. The user approved sheet-relative Evaluate measurement with the preserved shell reviewed separately. The final visual disposition is recorded in `finish-review.md`.

## Locked direction

The user selected **Shared evidence notebook**, `shared-notebook`, comp build path. Its approved image and prompt sidecar are in `.impeccable/mocks/decision/issue-198-shared-notebook.*`. Surface seed `d6060110` derived seven compositions and dealt ranks 6, 2, 7; `concepts.json` and `options.json` retain the equal-salience options. The contract is `.impeccable/surfaces/evaluation.md`.

The user required preserving the existing design system. Shared campaign chrome and system typography remain governed by `DESIGN.md`. The generated portrait comp depicts mobile navigation at a raster width where the existing app uses a desktop sidebar. The first measured reproduction scored 61.2%; this mismatch is recorded, not waived or described as passing. No raster assets ship in the UI.

## Implemented behavior

- Independent Evaluate URL state, blank debounced finder, exact-number relevance before SQL paging, deliberate selection, direct links and preserved Roster/Place returns.
- Separate identity, bounded note/application history and complete active tag catalog regions; author-only versioned notes; atomic normalized create-and-apply with original attribution and cap/archive protections.
- Client-generated operation identities, immutable actor/tenant/fingerprint-bound receipts, 24-hour expiry, indexed global cleanup, replay after closing and provider retry transactions.
- Tab-scoped exact-payload recovery before dispatch, draft navigation protection, owned asynchronous state, read-only Closed views and authoritative reopen refresh. Retained Roster drawer consumers use the same contracts.

## Guidance actually read

- `AGENTS.md` and `.github/instructions/`: C# conventions, Blazor architecture, UI design, Bootstrap theme, service layer, validation, API endpoints, EF tenancy, season lifecycle, placement decisions, testing and observability.
- `PRODUCT.md`, `DESIGN.md`, confirmed evaluation surface brief, merged workspace/Roster handoff, #198 and parent/dependency context supplied in the implementation plan.
- `.agents/skills/add-feature-slice`, `add-api-endpoint`, `add-domain-persistence`, and `add-blazor-ui`: their skills and applicable input, service, HTTP/WASM, EF query/retry/migration, lifecycle, rendering, binding, form and JS references.
- `.agents/skills/nova-testing` and component, integration and browser-suite references; installed .NET test-generation and run-tests skills. Test agents retain research/matrices in their reported temporary workspaces.
- `.agents/skills/impeccable/SKILL.md`; `reference/new-work.md`, `shape.md`, `visualize.md`, `craft-floor.md`, `polish.md`, `audit.md`, `clarify.md`, `document.md`; built-in imagegen skill for decision comps.
- Aspire router, orchestration, browser validation and Playwright CLI skills; `.github/pull_request_template.md`.
- Separate reviewer read the code-review skill and applicable repository boundary guidance; its findings and dispositions are in `local-code-review.md`.

## Validation

The subsequent [PR review round 1](review-round-1.md), [round 2](review-round-2.md), [round 3](review-round-3.md), [round 4](review-round-4.md) and [round 5](review-round-5.md) records supersede the application/test validation below for their source changes and link separate local reviews. The original design and measurement evidence remains applicable.

Build-capable commands are serialized. Aspire-backed suites are serialized across the machine; all test commands use `--no-build`. Raw command logs are ignored local artifacts under this directory.

| Check | Latest result |
|---|---|
| `dotnet build Nova.slnx` | Build 33 passed, zero warnings/errors (`381d5019`). |
| `dotnet format Nova.slnx` | Final verify round 6 passed, no changes required (`381d5019`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Round 6: all 2,946 passed, including duplicate search submission, explicit retry, held identity-refresh feedback, storage/expiry and lazy Roster loading (`381d5019`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Final: all 598 passed (`381d5019`). |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Round 6: all 148 passed, zero skipped, with accessibility captures enabled (`381d5019`). Prior failed/aborted runs are retained below. |
| `npm run check:contrast` from `Nova/` | Passed theme contrast and Bootstrap-blue checks. |
| `node --check` on Evaluate/drawer JS modules | Passed. |
| `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Passed: no pending model changes. |

Initial failures are retained as diagnostic evidence, never represented as passing checks. No tests or checks were skipped to make the change pass, and no diagnostic suppression was added. Optional browser accessibility checks are enabled for the completion run with `NOVA_A11Y_SCREENSHOTS=1`.

## Separate review

`evaluation_boundary_review` reviewed the complete production diff, including untracked files, against authorization, concurrency, recovery, HTTP and provider constraints. All reported production findings R1–R14 and the subsequent regional-feedback defect are source-resolved, including stale drawer ownership, UTC cursors, receipt expiry at commit, exact retained edit versions, storage restoration races and copyable rejected replay text. The final review of source `381d5019` verified the completed command evidence and records no unresolved code-review blocker in `local-code-review.md`.
Browser iteration 3 (build 21) was interrupted after remaining active for over 19 minutes without a final summary. It reported a canonical Roster navigation timeout; the 1,000-participant evidence completed in 1,952 ms. This aborted run is not counted as a passing suite. Ctrl+C stopped its scoped AppHost and containers; the subsequent Aspire stop reported no active AppHost.

The build-28 full browser run (browser-final.log) was interrupted after about 12 minutes without a final summary or reported assertion failure. It is not counted as passing. Four existing held-route tests lacked failure-path release; finally blocks now release and unregister those routes without changing assertions. This cleanup defect is not a proven explanation of the stall. The next run enables individual results and xUnit long-running diagnostics. A help probe using an extra '--' hit a native MTP CLI help-protocol error; direct --help succeeded, and its supported flags are used.

Build 29 passed with zero warnings/errors; unit round 4 passed 2,943/2,943 and format round 4 verified clean. Browser round 4 completed 147 of 148 cases (145 passed, two failed) before interruption of the remaining case. Discovery/result comparison identified PlacementsTabAllowsApprovedMemberToSaveAsync selecting nonexistent option value NotSelected against numeric value 2, repeatedly waiting 30 seconds for up to 60 attempts. The selector is corrected. The two reported failures were drawer rejection feedback hidden during identity refresh (production correction) and a new Ctrl-click test waiting on an opener-specific Popup event (changed to context Page, retaining all behavioral assertions). Final rebuilt suite results remain pending.

Browser round 5 completed 147/148 passing. Its only failure was the 1,000-player read still loading at Playwright's implicit five-second assertion window. The new same-URI search guard prevents repeated submission from restarting the identical destination; two unit regressions cover pending reads and explicit retry. Separate review approved a finite 15-second limit only for this scale-read settlement, because no five-second SLO was specified. The check fails immediately on a retrieval error, preserves exact full count text, URL, 20 rows and page 2/50, and records actual elapsed time with LatencyTargetSpecified=false. No search retries, global timeout increases, skips or suppressed failures were added. SQL evidence shows latest-decision ROW_NUMBER ranking in these query paths; earlier expectation of a lateral translation is not runtime evidence.

Final source validation: build 33; format round 6; 2,946 unit, 598 integration and 148 browser tests passed with zero skipped cases. Browser round 6 completed in 3m 26s. Its separate 1,000-player lookup measured 1,988ms with exactly 999 matches, 20 results and page 2/50. Final portrait comparison reproduces the reviewed 73.43% overall score; its original regional failures remain preserved. The user subsequently approved sheet-relative measurement and separate shell review. `sheet-relative/manifest.json` records that approval, native border registration and explicit viewport coverage; `sheet-relative/report.json` scores 76.11% against the unchanged 72% overall threshold. Raw regional labels are preserved and individually reviewed in the final finish disposition. Older history is reviewed in full-page captures without claiming a same-scale quantitative score.

## Polish, accessibility and copy

The subsequent [instructions and skills review](instructions-hygiene-review.md) records focused guidance updates, independent review and repeated format/unit validation without changing application source.

The single detector pass in `detector.json` reported nine advisories. The implemented correction replaced off-scale radii with .25rem/.375rem, the finder heading with 1.25rem, the identity's fluid lower endpoint with 2rem, and the three .8rem metadata declarations with .875rem. No detector rule was disabled and no second detector scan is claimed. The retained `detector-status.txt` reflects the then-open hero gate, not the final disposition.

The bounded visual correction batch restored compact context, trait disclosures, directional icons, Paper White inputs and 44px note ownership controls. Passing browser checks and authentic portrait, landscape, desktop, Closed and own-note captures cover visible focus, target sizes, long content and the separate phone stages. Contrast checks passed. Reduced-motion preferences disable the short sheet arrival animation. Copy distinguishes shared notes, explicit Save and Find another actions, campaign-local versus season placement, Closed read-only state, and retained recovery text. Separate local review covers the corresponding behavior; no new source change was introduced for the final measurement scope.
