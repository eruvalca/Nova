## Summary

<!-- Describe the concrete problem and resulting behavior. Link the issue. -->

## Validation

- Validation record: <!-- Link the single durable record required by AGENTS.md.
  Keep tested revisions, commands/results, transition evidence, failures, review
  dispositions, and limitations there; update it after each review round. -->

## Checklist

<!-- AGENTS.md → Build & validation is authoritative. Before opening and before
     merge, all three suites must pass locally. On intermediate pushes use the
     affected-suite policy (unit always; integration for provider/HTTP/EF changes;
     browser for interactive UI/markup/CSS/JS changes; all three when in doubt).
     CI does not run the integration or browser suites. -->

- [ ] Build and format checks pass.
- [ ] Unit, integration, and browser results satisfy the current PR-stage gate.
- [ ] Applicable migration-model and design checks pass; limitations are recorded.
- [ ] Applicable guidance and sibling paths were checked; required local reviews and all human/bot findings, including suppressed findings, have evidence-backed dispositions.
