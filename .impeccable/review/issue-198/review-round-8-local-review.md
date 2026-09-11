# PR #253 — round 8 independent local review

Reviewed staged retention changes against `b577001319b3302da723d93de60356852b3ba00b`, the two available automatic findings, and current source/tests. No application or test changes are staged in this round.

## Findings

- Archived application validation finding is inapplicable: HttpCampaignEvaluationQueryService line 26 already requires `!IsArchived || !CanRemove`. ApplicationsRejectInvalidNestedEvidenceAsync explicitly rejects archived-removable payloads. Changing to the expression quoted by the finding would introduce the defect it alleges.
- Singular result-copy finding is inapplicable: “1 player matches” and “N players match” have correct subject–verb agreement. Existing component assertions cover the singular wording. No code or test alteration is warranted.
- Archive checksum ambiguity was corrected during review: the archive index now distinguishes pre-archive working-tree SHA-256, canonical Git blob IDs, and the whole ZIP checksum; exported text line endings can differ. No evidence-content loss was identified.

## Retention verification

Verified all 44 indexed archive paths against the pinned commit's Git blob IDs. All 27 restored staged blobs equal base `3e0253c28677838858b1887b2351a133eac7bc16`; all 17 archived paths are absent from the index and their retained local files match the recorded working-tree SHA-256. Round 7 records remain tracked; the broader ignored raw manifest's round 7 entries were excluded from this final-set audit.

Independently verified ZIP SHA-256 `3E9FF14BF435E0B0004C71655257DE45AC9A885DD14142750BBE5FA5F2FE9705`. All 44 entries preserve pinned Git content: 20 byte-identical entries and 24 text entries differing only by line endings. The working-tree hashes are intentionally not represented as raw ZIP-entry hashes.

Inspected DESIGN, README, finish and shell references, plus the archive index and ignore changes. Ten retained Markdown sources have no broken relative links against the staged index. Archived measurement/history links use the immutable commit; restored generated files are explicitly baseline artifacts rather than current Evaluate evidence. Ignore checks cover the removed historical records and JSON. Approved comp/provenance, current captures, finish/shell decisions and current validation remain tracked.

Projected PR diff before this concise review record: 235 files, 16,479 additions + 2,984 deletions = 19,463 changed lines. Reduction comes from archived historical evidence and reverting generated-file churn; no application/test removal, minification, skipped assertions, or relaxed design/validation gate.

## Guidance and validation

Applied AGENTS retention/review rules, existing C#/API/validation/testing guidance and feature recipes previously read, and Impeccable's evidence-retention rule. Reconsidered their applicability for the client validator, copy and documentation-only archive operation.

Inspected `.impeccable/archive/round8-build.log`: zero warnings/errors, 2m40.04s. Inspected `round8-unit.log`: 3,126 passed, zero failed/skipped, 36.847s. Final format exit 0 was directly observed by the parent; `round8-format.log` has no diagnostics, and its empty contents alone are not exit evidence. No command result remains pending. This reviewer ran only read/hash/archive/index/link inspections and edited this review record. Prior round's incomplete full-browser gate remains a pre-merge requirement; this cleanup does not establish a browser pass.

No remaining actionable finding in the inspected source or staged retention batch.
