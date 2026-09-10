# Instructions and skills review for PR #253

Reviewed the implementation and its recorded failures against [Microsoft's instructions-hygiene guidance](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/). The review keeps consequential local constraints, removes duplication and stale examples, and places conditional procedures in existing skill references. It does not convert every defect into a repository-wide rule.

## Decisions

| Action | Decision and evidence |
|---|---|
| Keep | Tenant authorization, current membership checks, immutable receipts, strict HTTP/WASM contracts, owner-scoped async work, separate local review and all local PR test gates already have guidance. PR findings R1–R14 mostly call for applying those rules, not duplicating them. |
| Verify / narrow | Root commands now consistently use build first, then `--no-build`. Preserve machine-wide Aspire serialization, explain shared capacity concisely, and keep source/assets fixed during a browser run so its evidence identifies a build. |
| Fix routing | Blazor rules now match `Nova.UI/wwwroot/js/**`; tenancy and service rules match mutation executors. The evaluation guard and transaction executor otherwise fall outside the filename-based routing despite owning those behaviors. |
| Clarify existing UI references | Listener replacement preserves per-owner browser-protocol state; shared scoped ES modules are legitimate. Evidence recovery remains independent of mutable history rows and delayed storage restoration. Mutation feedback must remain rendered while a refresh is pending, as demonstrated by R15. |
| Add focused persistence examples | Existing retry guidance now points to evaluation expiry/recovery and cleanup; it distinguishes the feature's 24-hour UUID-derived lifetime from universal mutation policy. The query reference requires observed provider translation for performance claims instead of inferring a lateral plan from LINQ. |
| Move / verify testing detail | The test instructions point to the existing browser reference instead of repeating its pitfalls. That reference retains the detailed knowledge, adds the validated diagnostic command, and removes a Bash-style environment command from a PowerShell block, the stale claim that no Close endpoint exists, and a Roster-specific two-page assumption from shared seeding guidance. It distinguishes 44px phone design requirements from older 24px baseline assertions. |
| Clarify design preparation | Establish viewport, DPR, mandatory shell content and a supported comparison frame before comp lock/production. Preserve original evidence and explicit approval when an already locked scope changes. No scoring threshold, script, review gate or approval authority is weakened. |
| Remove | Impeccable's generic persona and motivational opening adds no local decision information. Its mode, visual authority, craft references and bounded review workflow remain. |
| Do not add | No new AGENTS file, instruction file, custom agent or skill. Existing UI, persistence, testing and Impeccable routes cover the work. No universal tag cap, recovery lifetime, 15-second timeout, browser-API requirement, or evaluation-specific color rule was introduced. |

## Guidance and evidence inspected

Read `AGENTS.md`, matching instruction routing, Blazor/testing/UI-design guidance, and the relevant existing UI state/interop, persistence retry/query, testing/browser and Impeccable new-work references. Applied the installed `skill-creator` guidance for scoped skill edits and the installed .NET `run-tests` guidance for validation commands; the repository run-tests overlay is absent. Reviewed the PR's `local-code-review.md`, final visual disposition, source contracts and named implementation examples. The independent reviewer also inspected the affected source/test examples.

Both repository Impeccable copies were updated. Their changed bodies/references match after existing provider-specific metadata, path and command-prefix substitutions. No custom agent definitions or hooks changed.

## Validation

- Application/test source remains exactly `381d501950d064d426ddce7c772fe449c485cfde`; this follow-up is guidance and records only, based on PR head `d39517ee`.
- `dotnet format Nova.slnx --verify-no-changes`: exit 0, no changes (`guidance-format.log`).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: exit 0; 2,946 passed, zero failed/skipped (`guidance-unit.log`). Uses the previously validated unchanged build. Integration/browser results remain those at `381d5019`; no provider or UI source changed in this follow-up.
- Skill creator `quick_validate.py`: passed for `add-blazor-ui`, `add-domain-persistence`, `nova-testing` and canonical `impeccable`.
- Changed-file Markdown link targets, shared-JS/executor glob matches, provider-normalized Impeccable copy comparison, and `git diff --check`: passed. A naive byte-for-byte copy check initially failed on existing provider substitutions; the normalized comparison passed without altering those substitutions.
- Separate `guidance_hygiene_review` agent: no remaining findings after correcting a module-state wording conflict and removing an incorrect example type name. Static review only; it did not run the app or tests.

These checks verify consistency, referenced evidence and syntax; they do not prove improved performance on a future agent task. Reassess the guidance on subsequent work rather than adding speculative rules now.
