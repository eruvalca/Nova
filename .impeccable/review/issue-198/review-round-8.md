# Review round eight — reviewability and evidence retention

Base `b577001319b3302da723d93de60356852b3ba00b`; CI Build and Unit Tests passed.
Automatic review `5181018931` stopped at the 20,000-line cap. Session
`840e8951-aeb7-4cfe-9675-2b0108ffae68` and workflow `34619762076` expose the same two
stored findings. All paginated review/comment responses were inspected; all 17
threads are resolved with no further nested-comment page. No new inline thread.

| Stored finding | Independently verified disposition |
| --- | --- |
| HTTP application validator allegedly rejects archived evidence | Inapplicable: line 26 already uses `!application.IsArchived || !application.CanRemove`, exactly the suggested fix. Archived/removable combinations are rejected; valid archived evidence is allowed. Existing `ApplicationsRejectInvalidNestedEvidenceAsync` covers the invalid combination. |
| Singular result allegedly has incorrect grammar | Inapplicable: “1 player matches” uses singular subject and verb; “N players match” uses plural agreement. Existing tests assert singular output. No copy change or duplicate test is justified. |

The separate reviewer checked current code, existing tests and both raw findings,
not only the implementer's summary. Neither suppression nor the failed review is
treated as resolution or approval. No manually requested review is permitted.

## Retention correction

The full diff had 31,072 changed text lines. Generated Impeccable build/scaffold and
diff-report churn contributed 6,956. After preserving their current versions in
immutable commit `b5770013` and a local ZIP, restore these previously tracked artifacts
to merge base `3e0253c28677838858b1887b2351a133eac7bc16`. They remain baseline
artifacts, not current Evaluate evidence; no unrelated repository-wide untracking.

Archive the large sheet-relative JSON inputs/results, old archive manifest, original
local code review and rounds one through six at that same pinned commit. Keep their
local files for resumption, with narrow ignore rules. [The archive index](evidence-archive.md)
lists each Git blob and SHA-256 and provides direct shared links. Current approved
comp/provenance, captures, finish/shell decisions, instructions review, round-seven
validation and current records remain directly reviewable. Update DESIGN and all
retained Markdown references to the immutable records. The original README's complete
validation history is [also retained](https://github.com/eruvalca/Nova/blob/b577001319b3302da723d93de60356852b3ba00b/.impeccable/review/issue-198/README.md).

No application, migration, test, diagnostic or quality gate is removed or weakened;
JSON is not minified to evade the cap. This follows the existing AGENTS retention
rule; no new rule or skill is needed. The separate reviewer endorsed the scoped
archive approach and required link/archive verification before push.

## Validation

Read current AGENTS retention/review rules, existing API/Blazor and testing recipes,
the review watcher protocol, and the PR template. Application and test source is
unchanged from `b5770013`; round-seven evidence and its explicit full-browser
limitation remain applicable. Archive checks, build/unit and format validation are
pending. This round will use one combined commit, followed by fresh CI and automatic
review. No clean review or merge readiness is claimed.

Final checks passed: `dotnet build Nova.slnx` (zero warnings/errors, 2m 40.04s),
`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` (3,126 passed,
zero failed/skipped, 36.847s), and `dotnet format Nova.slnx --verify-no-changes` (exit 0).
Raw logs remain local under `.impeccable/archive/round8-*.log`. No integration/browser
rerun is required for these documentation/retention-only changes; no new pass is claimed.
The [separate review](review-round-8-local-review.md) verifies all 44 archived entries,
27 restored base blobs, 17 preserved local files, ZIP contents/checksum, ignore rules
and retained links. Application/test diff against `b5770013` is empty. The final PR
diff is approximately 19,500 changed lines, below the unchanged 20,000-line cap.
