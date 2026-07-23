---
name: aspire-playwright-validation
description: >-
  Run a repeatable Aspire + Playwright manual validation pass for Nova UI flows.
  USE FOR: ticket acceptance that requires browser-level validation of interactive behavior,
  auth/claims propagation, and role-based UI controls. DO NOT USE FOR: replacing existing
  unit/integration tests or broad exploratory testing without a concrete scenario.
---

# Aspire + Playwright Validation

Use this skill when a feature is already implemented and the remaining evidence required is a
browser-level validation pass against the Aspire-hosted app.

## Preconditions

- The feature has targeted unit/integration coverage in place.
- You have a concrete scenario list (for example: admin create/edit/archive/restore, evaluator
  read-only checks).
- You are running in a worktree session (use isolated Aspire start).

## Execution Workflow

1. Start the app:
   - `aspire start --isolated --non-interactive`
2. Wait for readiness:
   - `aspire wait nova --non-interactive`
3. Discover the frontend URL:
   - `aspire describe --format Json`
4. Run Playwright interactions against the discovered URL.
5. Capture only actionable outcomes:
   - flow steps completed
   - user-visible blockers found
   - code changes made to resolve blockers
6. Stop apphost:
   - `aspire stop --non-interactive`

## Required Guardrails

- Do not use `dotnet run` for AppHost startup.
- Do not poll endpoints manually before `aspire wait`.
- Do not guess URLs; always read from Aspire state.
- Keep artifact hygiene: remove temporary files created only for browser automation (for example,
  ad-hoc upload images in repo-local temp folders).
- If a blocker is found, fix it and rerun the affected browser segment before concluding.

## Deliverable Format

Report outcomes with:
- scenario coverage reached
- blockers discovered (if any)
- exact files changed to fix blockers
- final apphost/browser cleanup status
