# PR #253 review watch

## Scope and current round

- Round one was pushed once as `af99924213e2cbf455bbfca12cef5551f5bc36db`; all six threads were explained and resolved after the push, and the three withheld findings were covered in the PR summary comment. Both CI checks passed.
- Fresh review `PRR_kwDOSz2VcM8AAAABNEZlCg` recommends changes. [Round two](review-round-2.md) collects all six new threads and eight matching raw stored entries. Production corrections passed all three local suites; the single combined round-two commit will carry final validation evidence. Its fresh remote CI and automatic review remain the next gate. No review was requested manually.

## Round-one history

- PR: https://github.com/eruvalca/Nova/pull/253
- Watcher: `watch-nova-pr-253`; use GitHub MCP or GitHub CLI for all further GitHub operations.
- Round starts at `63235f98d61d58d4dd80c3f19f057206852ccde4`; Build and Unit Tests succeeded.
- All round-one fixes and local validation are complete. This record is included in the single combined review-fix commit; fresh remote CI and automatic review are the next gate.
- First review: `PRR_kwDOSz2VcM8AAAABNDwRgg`, six posted threads; its eight stored entries duplicate those findings.
- Second review: `PRR_kwDOSz2VcM8AAAABND4x3w`, zero posted comments but three actionable stored findings. Its “Needs a closer look” summary does not clear these findings.
- Session evidence: [first](https://github.com/eruvalca/Nova/sessions/2003ace3-8aae-41cd-9b99-7ac5aaec19ec), [second](https://github.com/eruvalca/Nova/sessions/ce410964-c5b0-46c7-92e5-c27c0266ddcf). Stored inputs inspected before the user's CLI/MCP-only preference.

## Findings collected

| Finding | Source | Implemented disposition |
| --- | --- | --- |
| Retention worker must survive non-provider failures | Thread `PRRT_kwDOSz2VcM6hNrSq` | Catch/log non-shutdown failures around the complete pass; propagate shutdown cancellation. |
| Drawer notes and applications fail independently | Thread `PRRT_kwDOSz2VcM6hNrTi` | Regional transport handling with owner/sequence checks and cancellation propagation; inspect Evaluate siblings. |
| PUT payload documentation | Thread `PRRT_kwDOSz2VcM6hNrTx` | Document content, expected version and operation identity. |
| Receipt immutability diagnostic | Thread `PRRT_kwDOSz2VcM6hNrUC` | Use generic mutation-receipt wording. |
| Tag removal response documentation | Thread `PRRT_kwDOSz2VcM6hNrUh` | Document 200 with immutable receipt. |
| Note edit/delete response documentation | Thread `PRRT_kwDOSz2VcM6hNrVD` | Document 200 with immutable receipt; inspect sibling create docs. |
| Unchanged successful edit version accepted | Second session stored 001 | Reject unchanged edit version; preserve delete's matching-version contract. |
| Historical attribution disappears after membership/account changes | Second session stored 002 | Persist original author names at note/application creation; project snapshots in Evaluate, Roster and player history; incremental migration. |
| Invalid optional placement participant emitted in URL | Second session stored 003 | Omit nonpositive optional participant values. |

The PR description's “Close #200” wording accidentally registered #200 as a closing reference. It now says “Close redesign (#200)”; CLI verification reports only #198 as a closing reference.

## CLI access to stored review comments

`gh agent-task view <session-id> --log` currently renders only setup lines for these reviews. Its raw log contains the stored findings. Verified through the installed CLI's request metadata and the official `cli/cli` CAPI client source:

- List sessions using `gh api` against `https://api.individual.githubcopilot.com/agents/resource/pull/4499142493`.
- Read a session and its raw SSE log at `/agents/sessions/<id>` and `/agents/sessions/<id>/logs` on that host.
- Use the same headers as the CLI: `Copilot-Integration-Id: copilot-4-cli` and `X-GitHub-Api-Version: 2026-01-09`, with the current GitHub credential obtained through `gh auth token` in a local variable. Never print or persist the credential; clear the variable afterward. Direct `gh api` needs explicit authentication for this separate GitHub-owned API host.
- Parse `data:` JSON events and inspect `choices[].delta.tool_calls[]` where `function.name` is `store_comment`; decode the JSON arguments. Compare with posted threads and review bodies rather than counting posted comments alone.

The second session's three stored comments were independently recovered through this CLI path. This avoids further browser use and preserves access to withheld findings in subsequent rounds.

## Completion protocol

Validate the combined round, obtain separate local review, push one commit, explain fixed or inapplicable findings in the PR, and resolve only those addressed threads. Wait for successful CI and fresh automatic review without requesting one. Older actionable findings and suppressed findings remain obligations. Stop only at the user's approval condition or clean “closer look/human reviewer” exception; then update related issues as needed and pause the watcher. Do not merge without explicit authorization.
