# PR #253 — review round 4 local review

## Scope and disposition

Separate local review by `round1_local_review` against `339ee66e3e3fdd13e67dd74a28795f22c30bdb05`. Inspected the actual shared history guard, both evidence-surface callers and adapters, their owner/reset paths, six endpoint-name constant extractions, 15 component regression cases, and 14 controlled browser protocol cases. No remaining actionable findings or outstanding required local checks in this bounded review. This is neither GitHub approval nor a fresh review of the whole PR.

The reviewer read source and diffs, ran no builds or tests, contacted neither GitHub nor the browser, and edited only this review record. The implementer's summaries supplied context rather than proof.

## Findings and dispositions

- **Reported history race, confirmed and fixed:** `committed` previously returned success after input/pending state revoked the permit, and C# bypass flags could discard the resulting protection callback. The guard now requires both committed traversal and matching accepted `popstate`, with an independently settling revocation path. Both redundant C# callback-suppression flags are removed. Traversal rejection/throw returns false; callers retain owned retry feedback.
- **Medium, additional sibling finding, fixed:** abandoning a delayed release after new input could leave JavaScript `released=true`. Both callers now cancel an unsuccessful departure while its in-flight slot remains owned. The shared cancellation export verifies owner/lease and revokes replay/release state; old cleanup cannot revoke a replacement owner's guard.
- **Medium, additional serialization finding, fixed:** Evaluate originally acquired its in-flight slot only after awaiting draft persistence, allowing duplicate Discard actions through that first await. The slot now covers the whole persistence/release/departure sequence and is cleared by owned cleanup. Drawer already acquires its slot before awaiting its module.
- **Test boundedness gap, fixed:** controlled revocation scenarios deliberately leave commitment unresolved; an unbounded test await could hang on regression. The browser test now bounds completion to 15 seconds with the test cancellation token. A cancellation-export case also verifies actual shipped JavaScript behavior rather than relying solely on a bUnit invocation assertion.

## Source conclusions

- A replay has its own acceptance and revocation promises. Either commitment/event order succeeds only after both signals; input, pending work, detach or replacement invalidates it. Duplicate active requests cannot steal its permit. The already-current key settles without awaiting a nonexistent event. The unrelated `finished` rejection from an enhanced fetch is observed without falsely rejecting an accepted committed traversal.
- Callers capture departure request and owner/context, recheck after awaited persistence/module/release work, and preserve a newer prompt, draft or owner. Pending mutation payloads remain independent and are not cleared by departure recovery. Error feedback is scoped to the current departure.
- The analyzer-driven `FinishDrawerDepartureAsync` extraction retains the exact owned cleanup ordering. The post-module `DrawerMutationBlocked` check uses the existing predicate to recheck busy, pending and recovery-storage readiness before discarding or releasing; it is broader than merely rereading the stored-operation field. Brace fixes do not alter behavior. No new diagnostic suppression was introduced for these adjustments.
- The six endpoint-name constants preserve their existing string values. Handler binding, authorization, route templates, response metadata and wire contracts are unchanged, including the three sibling effective-placement/season names.

## Tests reviewed

The 15 new component cases use rendered controls and the real owner/lease supplied during attach. They cover false/throwing history replay, protection callbacks before and after completion, old release completion/failure after owner change, fresh draft during delayed release on both surfaces, and duplicate Evaluate Discard during held persistence. Assertions check exact replay keys, release/cancel owners, draft/prompt retention, scoped error feedback, and one departure after the single held write.

The 14 browser protocol cases import the shipped module into an isolated app-origin document with a controlled Navigation API. They test commitment/event ordering, input and pending revocation, revocation after either partial signal, synchronous throw, rejected commitment, detach/replacement, duplicate/current-entry behavior, rejected `finished`, and owned cancellation of an already released guard. Ordinary browser navigation remains covered separately by the native UI scenarios; the controlled API is not presented as native-history evidence.

## Validation

The staged application/test patch is recorded in `round4-tested-source.log` against the base above. Independently checked SHA256: `F181E061C53EBD7167FA97E4BC31CE23F96351FB1F96C2C1E517B578366DA98C`.

| Evidence inspected | Result |
| --- | --- |
| `round4-build-complete.log` | Build succeeded; 0 warnings, 0 errors. |
| `round4-unit.log` | 3,020 passed; 0 failed, 0 skipped; 18.058s, including the 15 new component cases. |
| `round4-format-verify.log` | Recorded verification exit 0; implementer attributes it to completed session 30065. |
| `round4-integration.log` | 600 passed; 0 failed, 0 skipped; 1m 47.365s. |
| `round4-browser.log` | Full suite: 162 passed; 0 failed, 0 skipped; 3m 33.214s, including the 14 controlled protocol cases. Implementer reports `NOVA_A11Y_SCREENSHOTS=1`; native UI and capture checks were included. |

Commands were run by the implementer and results inspected from these records. The patch fingerprint was rechecked after the final suite results and is unchanged; the implementer confirms source/assets remained fixed. No pass is inferred from source inspection.

## Guidance actually read

Reused the applicable instructions already read in this reviewer context: root `AGENTS.md`; C#, Blazor architecture, testing, UI, validation, API endpoint, service, tenancy and lifecycle rules. Reused `add-blazor-ui` lifecycle/state and render-mode references, and re-read its JS-interop reference during this round. Also reused the four `add-api-endpoint` references, producer-to-UI HTTP guidance, `nova-testing` component and browser references, and the personal `code-review` skill/local doctrine. Earlier records list the complete prior source inventory. The browser reference's new verified-interaction paragraph was read in round 3.

Inspection used scoped `git diff`, `git rev-parse HEAD`, `rg` searches, and `Get-Content` reads. No skill was treated as read merely because it appeared in the catalog.
