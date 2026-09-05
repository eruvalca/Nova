# Design evidence for new work

This policy applies prospectively. Keep all existing historical design evidence in Git. Read old
comps, specifications, captures, and reviews as inputs; never overwrite the existing
`.impeccable/build/`, `review/`, `mocks/`, or `critique/` trees during a new run.

Before generating new design evidence, allocate one run and retain its returned ID:

```text
node .agents/skills/impeccable/scripts/design-run.mjs start
node .agents/skills/impeccable/scripts/design-run.mjs exec <id> build-phase.mjs start --comp <approved-comp>
node .agents/skills/impeccable/scripts/design-run.mjs exec <id> comp-spec.mjs --comp <approved-comp> --grid
```

Use `exec <id> <tool.mjs> ...` for the existing artifact tools throughout that run. It sets the run's
paths and runs from the Git root; it does not change the tool's behavior or authorize image/API
spend. The allowlist covers build-phase, comp-spec, comp-diff, font-match, generate-image,
embed-prompt, concept-seed, critique-storage, and context. Ordinary application edits and live-mode
operations keep their existing workflow.

New build/spec/crop, mock, review/diff, and critique defaults live below
`artifacts/design/<id>/`, which is local working evidence. Supply those paths for explicit `--out`
arguments, browser captures, native image tools, decision-card comp slots, and reviewer handoffs.
In older playbooks, `.impeccable/build`, `review`, and `mocks` examples describe these same roles:
substitute the current run's directory for writes. Historical input paths remain valid. A new run
gets a fresh directory; use the existing ID only when deliberately continuing the same work.

At handoff, after the final captures and behavioral checks correspond to the final source:

```text
node .agents/skills/impeccable/scripts/design-run.mjs finish <id>
```

Copy this curated evidence into a new `.impeccable/evidence/<id>/` directory and include it in the
change's Git diff:

- The approved comp/reference and its exact prompt/provenance sidecar, including approval context.
- Final captures for the relevant viewports, routes, and UI states.
- The reviewer's disposition and any accepted limitation or remaining gap.
- `source-start.json` and `source-final.json`, plus a short mapping from the retained filenames to
  their source run paths and the revision/commands actually checked.

`finish` records source identity; it does not assert that images match source or that verification
passed. Check those relationships before curating. Keep intermediate crops, exploratory candidates,
diff heatmaps, repeated captures, and raw logs in the local run directory. Preserve a particular
intermediate item only when it is necessary to explain an accepted decision. This requires no new
approval dialogue when the session already authorizes the work.
