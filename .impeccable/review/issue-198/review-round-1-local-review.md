# PR #253 — review round 1 local review

## Scope and disposition

Separate, read-only source review by `round1_local_review`, followed by this documentation-only record. Reviewed the working-tree changes against `63235f98d61d58d4dd80c3f19f057206852ccde4`; the implementer will bind the tested source to the single review-round commit. No remaining actionable findings in this bounded change set. This is a local code-review disposition, not a GitHub approval or a new review of the entire implementation PR.

The review followed actual implementations, callers, sibling loaders, persistence writers, HTTP consumers, and regression code. It did not rely on the implementer's summary as proof. The reviewer made no application/test edits, ran no builds or tests, and contacted neither GitHub nor a browser.

## Findings and verified corrections

| Finding | Disposition |
| --- | --- |
| Medium: drawer trait-choice transport failures still escaped before notes/applications could load. `LoadDetailAsync` awaited `LoadTagChoicesIfNeededAsync`, whose catalog loader lacked a transport boundary. | Fixed. The catalog now records a regional error with request/owner checks, allows evidence loading to continue, and propagates component-owned cancellation. Identity transport failures also use the existing detail retry path. Source and rendered-control regressions reviewed. |
| Medium: an obsolete persisted-state restore could start a new owner's history request after an awaited catalog or notes operation. | Fixed. `RestorePersistedStateAsync` snapshots both owner and detail request sequence and checks them, plus disposal cancellation, between awaits. `LoadDetailAsync` has the corresponding guards. The synchronous `ApplyDetailResult` extraction preserves result projection and persistence behavior. |
| Evidence gap: the first stale-restore regression released the old operation only after new notes had already populated persisted state. | Fixed. Both normal/restored variants now hold the new owner's notes pending, await old startup completion, verify exactly one notes request and no applications request before releasing notes, then verify the current evidence. This exercises the reported ordering rather than only the settled screen. |

An observation about pre-existing cancellation handling outside these loaders was scoped explicitly: finder supersession and mutation recovery were not changed or claimed as comprehensively reviewed by this correction. No stale-state defect was identified in those existing guarded catches.

## Source conclusions

- **Cleanup:** `RunPassAsync` contains scope creation, service resolution, context creation, pruning and disposal in the non-shutdown exception boundary. A failed pass logs and permits another pass; shutdown cancellation propagates. The pruning predicate and batch bound are unchanged.
- **Independent UI recovery:** notes, applications and catalog failures retain successful neighboring regions. Every changed catch checks request ownership before publishing an error. Original request checks prevent old orchestration from starting work for a replacement owner. Component-owned cancellation escapes the changed loaders.
- **Historical attribution:** both production creation paths capture the actor's display name within the existing transaction, after membership authorization/locks and participant lifecycle checks. Ordinary tag application and atomic create/apply share the creation path. Note edits and already-applied outcomes do not rewrite the original name or actor identity. Evaluate/Roster evidence and player detail project stored snapshots without historical tenant-filtered user lookups. Tenant visibility and mutation capability predicates remain intact.
- **Persistence:** required snapshot columns are covered by an incremental migration and model snapshot. No fallback, default or backfill was added, as required by the pre-release/no-legacy-data boundary. Fresh contexts and the established membership/aggregate lock order remain unchanged.
- **HTTP/recovery:** a successful note edit must return a nonempty version different from the expected version; delete still requires the expected version. These checks match the producer's edit/delete behavior. Operation receipt identity and recovery validation remain intact. The placement URL builder omits nonpositive optional participant IDs, while the HTTP client validates caller input before normalization.
- **Contract documentation:** PUT body and note/tag success documentation now describe expected versions, operation identity and receipt-bearing responses. The interceptor diagnostic accurately covers both receipt types.

No new authorization, tenant-disclosure, concurrency, recovery-identity or API-shape regression was found in the reviewed changes. No visual-layout approval is asserted by this code review.

## Validation evidence inspected

These are implementer-run results; the reviewer read the named logs and relevant regression source.

| Evidence | Result |
| --- | --- |
| `round1-build-final.log` | Solution build succeeded; 0 warnings, 0 errors. |
| `round1-unit-final.log` | 2,979 passed; 0 failed, 0 skipped, after the strengthened ordering regression. |
| `round1-integration.log` | 600 passed; 0 failed, 0 skipped. Production source was unchanged by the later unit-only regression strengthening. |
| `round1-browser.log` | 148 discovered: 141 passed, 0 failed, 7 opt-in capture checks skipped because the flag was absent. This run alone does not prove those checks. |
| `round1-browser-captures.log` | Enabled capture follow-up: 8 passed, 0 failed, 0 skipped. |
| `round1-format-final.log` | Final verification emitted no changes/errors; successful exit reported by the implementer. |
| `round1-model.log` | No model changes since the last migration; existing EF tooling/runtime version advisory remains informational. |

Regression source inspected includes cleanup failure/shutdown tests; drawer regional retry, late-owner, persisted restore and cancellation tests; changed-version HTTP receipt and optional URL tests; note/application creation, rename, departure/deletion and duplicate-application attribution tests; real HTTP attribution coverage; and the retained player-history projection regression. PostgreSQL race/replay suites were part of the successful integration run; this review does not claim newly injected races beyond the tests actually present.

## Guidance actually read

- Root `AGENTS.md` supplied in the task; instruction scope inventory read from `.github/instructions/`.
- `.github/instructions/`: `csharp-conventions`, `service-layer`, `ef-core-tenancy`, `api-endpoints`, `validation`, `blazor-architecture`, `testing`, `season-lifecycle`, `placement-decisions`, `functional-core`, `ui-design`, and `observability` instruction files.
- `.agents/skills/add-domain-persistence/SKILL.md`, plus `references/retrying-mutations-and-locks.md` and `references/query-construction.md`.
- `.agents/skills/add-blazor-ui/SKILL.md`, plus `references/lifecycle-and-state.md` and `references/render-mode-decision.md`.
- `.agents/skills/add-api-endpoint/SKILL.md`, plus all four required references: route constants, handlers/results, metadata/auth/antiforgery, and validation/ProblemDetails.
- `.agents/skills/add-feature-slice/references/wasm-client.md`, including the producer-to-UI contract check.
- `.agents/skills/nova-testing/SKILL.md`, plus the SQLite, Blazor component and Aspire integration harness references.
- `C:/Users/eruva/.agents/skills/code-review/SKILL.md`, plus its local-review, review-doctrine and review-checklist references. The local-review path was used; no GitHub review was posted.

Inspection used `git status --short`, `git diff --stat`, scoped full `git diff` reads, `rg` symbol/call-site searches, and `Get-Content` source/log reads. No finding required changing agent instructions or weakening a check.
