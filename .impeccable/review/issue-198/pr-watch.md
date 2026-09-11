# PR #253 review watch

## Current round

Round nine follows `fae6ed6753339174b6d58534b84157eb996fd480`: Build and Unit Tests
passed remotely. Review `5181303665` contains two actionable findings, so its
“Needs a closer look” summary does not satisfy the clean stopping exception.
[Round-nine validation](review-round-9.md) records complete posted/stored collection,
Place handoff correction, relevance coverage and the browser-conflict correction.
Build, full format and all three local suites pass (3,133 unit / 608 PostgreSQL /
176 browser, zero failures or skips). Fresh remote CI and automatic review follow
the combined push; both current threads already have pre-push explanations.
Watching is active. The user requires PR replies or a comment **before the combined
commit is pushed**; resolve threads only after the fix is available remotely.

[Historical evidence](evidence-archive.md) retains every prior round and failure.
The [previous watch record](https://github.com/eruvalca/Nova/blob/fae6ed6753339174b6d58534b84157eb996fd480/.impeccable/review/issue-198/pr-watch.md)
retains the original per-finding table and status history. No merge is authorized.

## CLI access to stored review comments

`gh agent-task view <session-id> --log` currently renders only setup lines for these reviews. Its raw log contains the stored findings. Verified through the installed CLI's request metadata and the official `cli/cli` CAPI client source:

- List sessions using `gh api` against `https://api.individual.githubcopilot.com/agents/resource/pull/4499142493`.
- Read a session and its raw SSE log at `/agents/sessions/<id>` and `/agents/sessions/<id>/logs` on that host.
- Use the same headers as the CLI: `Copilot-Integration-Id: copilot-4-cli` and `X-GitHub-Api-Version: 2026-01-09`, with the current GitHub credential obtained through `gh auth token` in a local variable. Never print or persist the credential; clear the variable afterward. Direct `gh api` needs explicit authentication for this separate GitHub-owned API host.
- Parse `data:` JSON events and inspect `choices[].delta.tool_calls[]` where `function.name` is `store_comment`; decode the JSON arguments. Compare with posted threads and review bodies rather than counting posted comments alone.
- Also inspect the session's Actions workflow log (`workflow_run_id` in metadata): ensemble reviews can expose only one member in `/logs` and `/events`. Compare every `Comment stored` location and the final classifier count with the review body. In round five, the raw session exposes one finding while the workflow confirms six. Do not infer completeness from the raw stored-comment count; record any unavailable full descriptions and assess the located source independently.

The second session's three stored comments were independently recovered through this CLI path. This avoids further browser use and preserves access to withheld findings in subsequent rounds.

## Completion protocol

Validate the combined round, obtain separate local review, and explain fixed or inapplicable findings in PR replies or a comment before pushing the single combined commit. Resolve addressed threads only after their correction is available remotely. Wait for successful CI and fresh automatic review without requesting one. Older actionable findings and suppressed findings remain obligations. Stop only at the user's approval condition or clean “closer look/human reviewer” exception; then update related issues as needed and pause the watcher. Do not merge without explicit authorization.
