# Stacked PRs integration decision

Status: implemented locally on 2026-09-15 (America/Chicago); remote pilot pending.

The active workflow is documented in [the runbook](../docs/stacked-prs.md) and
[Nova's skill](../.agents/skills/nova-stacked-prs/SKILL.md). This decision note
supersedes the research proposal; it is not a second operating manual.

- Keep one PR for coherent changes; use short stacks for useful dependent review
  boundaries, with working intermediate layers and tests beside their behavior.
- Keep one local agent responsible for the whole stack and its review loop.
- Use the upstream skill as a user-installed prerequisite for each agent; keep
  Nova-specific constraints in a shared repository recipe, routed from AGENTS.md.
- Preserve all existing validation and review gates for every layer. Separate
  rebasing from validation and publication rather than using routine sync.
- Use the existing PR template, native stack map, CI, and Copilot review rule.
  Add no new bot, hook, custom agent, or workflow for the first experiment.

Following Microsoft's [instruction-hygiene guidance](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/),
root instructions contain selection and routing. The recipe holds consequential
repository constraints; the linked runbook holds setup and examples. Upstream
mechanics remain in the separately installed upstream skill. At the user's
request, Nova does not vendor that skill. There is no provider-specific repository
copy or duplicate application validation checklist.

[The validation record](../docs/stacked-prs-validation.md) preserves source checks,
local exercises, corrected findings, implementation verification, and limitations.
[The first experiment](../docs/stacked-prs.md#first-experiment) defines the remaining
remote acceptance checks before wider adoption.
