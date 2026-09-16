# Guidance and evidence simplification

## Scope and revision

Validated on 2026-09-15 against working-tree guidance changes based on
`b42899e4b48810bccf05c9dedd0d7690ce343754`. No application, test, dependency,
CI, hook, or agent-definition source changed. This is a local documentation change;
no PR has been opened and no merge-readiness claim is made.
The follow-up review changed only `nova-testing/SKILL.md`, its
`references/transition-evidence.md`, and this record after the initial format check.

- [AGENTS.md](../AGENTS.md#completion-and-review) owns completion, review triage,
  the single validation record, and durable evidence retention.
- The [PR template](../.github/pull_request_template.md) links that record and
  confirms the existing stage-specific gates.
- The existing [feature](../.agents/skills/add-feature-slice/SKILL.md),
  [UI](../.agents/skills/add-blazor-ui/SKILL.md), and
  [testing](../.agents/skills/nova-testing/SKILL.md) recipes use one
  [transition evidence guide](../.agents/skills/nova-testing/references/transition-evidence.md).
  Testing instructions and the component-test reference route to it.
- Removed repeated suite commands, the duplicate UI self-check, the stale JSON-only
  club-creation example, and historical failure counts. Current multipart implementation
  references replace the copied client. No new skill was needed.

The approach follows the Microsoft article's emphasis on consequential local knowledge,
scoped instructions, and removing duplication. [Instructions hygiene](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/)

## Checks and results

| Check | Result |
| --- | --- |
| `git diff --check` | Passed. |
| `./scripts/Test-AgentGuidance.ps1 -SelfTest` | Passed: four agent families, hook-installer parity, and 17 self-test fixtures. |
| Skill Creator `quick_validate.py` on each of the three modified skills | Passed for all three. |
| Relative Markdown links and heading anchors in changed files | Passed; targets resolve. |
| `dotnet format Nova.slnx --verify-no-changes` | Passed after retrying with package restore access. |
| Build, unit, integration, browser | Not run for this local guidance-only edit. All opening/final PR gates remain required in AGENTS.md. |
| Migration-model, contrast, Impeccable surface checks | Not applicable to this change: no schema, application UI, Sass, or design artifacts changed. |

Skill validation used the bundled Python runtime and the installed Skill Creator validator.
Its missing PyYAML dependency was installed into `%TEMP%/nova-guidance-validator-deps`;
the validator then ran with that directory first on `sys.path` and outside the sandbox
to read those temporary files. Repository dependencies were unchanged. The initial
format attempt failed during sandboxed restore; the same command passed with restore access.

## Focused review dispositions

The full guidance diff was reviewed against the Microsoft article, its referenced rules,
and representative existing behavior. This was self-review, not an independent agent
evaluation or an application test run. Three findings were verified and corrected:

| Severity / confidence | Finding and disposition |
| --- | --- |
| Medium / verified | The new combined identity/lifecycle row said “Old data, confirmation, and feedback clear before replacement work finishes.” That contradicted the requirement to preserve a confirmed receipt through same-scope closure. Split identity ownership from lifecycle transitions and retained receipt feedback. Evidence: the [lifecycle recipe](../.agents/skills/add-blazor-ui/references/lifecycle-and-state.md#pending-command-recovery) and `CampaignPlacePanelTests.ConfirmedReceiptFeedbackSurvivesSameScopeCampaignClosure`. |
| Low / verified | Moving the matrix into a cross-tier guide broadened UI-specific labels: “Server validation” required a rendered form, and “Recoverable mutations” required browser storage. Scoped those rows to forms and commands retained across browser reloads; added service/HTTP input evidence and qualified pure-policy and required-refresh evidence. This preserves API-only work, ordinary mutations, and independent optional regions. |
| Low / verified | The testing skill's remaining “Boundary checks” repeated the HTTP, race, and query assertions in Testing Rules and the fault-injection reference. Removed that checklist; the skill already routes to both authoritative sources. |

The build/test gate section matches the baseline apart from its PR-template reference.
Design retention, comp limitations, suppressed-finding triage, and required original review
artifacts remain intact. The modified skills have no separate `.github/skills` copies.
Static walkthroughs covered a bounded API read, a form retry, server-only replay, a
same-scope closure, and an optional-history failure. No live agent forward-test is claimed.

## Guidance consulted

`AGENTS.md`; Skill Creator `SKILL.md`; the three feature/UI/testing recipes;
feature input, service-result, and WASM references; the bUnit reference;
testing, Blazor architecture, validation, and service-layer instructions;
Impeccable's existing evidence-retention rule; the .NET run-tests skill;
the PR template and CI workflow; the code-review skill and its local-review, doctrine,
and checklist references. Source comparisons included `IClubService`, `CreateClubInput`,
and `HttpClubService` for the obsolete transport example, plus the lifecycle recipe,
functional-core rules, Aspire fault-injection reference, and
`CampaignPlacePanelTests.Recovery.cs` for the follow-up findings.
