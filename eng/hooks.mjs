#!/usr/bin/env node
import { pathToFileURL } from 'node:url';
import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { repositoryRoot } from './lib/git.mjs';
import { completionDecision, rememberHint, startRequest } from './lib/checkpoints.mjs';

const EVENTS = { SessionStart: 'start', sessionStart: 'start', UserPromptSubmit: 'prompt', userPromptSubmitted: 'prompt', PostToolUse: 'edit', postToolUse: 'edit', Stop: 'stop', agentStop: 'stop' };
const PROVIDERS = new Set(['codex', 'copilot', 'vscode']);

export function normalizeEvent(raw, { provider, event } = {}) {
  if (!PROVIDERS.has(provider)) throw new Error('Specify --provider codex, copilot, or vscode.');
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error('Hook input must be a JSON object.');
  if (provider === 'copilot' && typeof raw.hook_event_name === 'string') provider = 'vscode';
  const kind = event || EVENTS[raw.hook_event_name || raw.hookEventName || raw.event];
  if (!['start', 'prompt', 'edit', 'stop'].includes(kind)) throw new Error('Unsupported hook event.');
  const session = raw.session_id || raw.sessionId;
  if (typeof session !== 'string' || !session) throw new Error('Native hook input did not supply a session ID; verification is unverified.');
  let input = raw.tool_input ?? raw.toolArgs ?? {};
  if (typeof input === 'string') {
    try { input = JSON.parse(input); } catch { input = { command: input }; }
  }
  if (!input || typeof input !== 'object' || Array.isArray(input)) input = {};
  if (!input.command && typeof (input.patch || input.input) === 'string') input = { ...input, command: input.patch || input.input };
  return { ...raw, provider, kind, session, cwd: raw.cwd || process.cwd(), tool_name: (raw.tool_name || raw.toolName || '').split('.').at(-1), tool_input: input, session_id: session, stop_hook_active: raw.stop_hook_active === true || raw.stopHookActive === true, readOnly: raw.agent_type === 'explorer' || raw.agentType === 'explorer' };
}

export function contextOutput(provider, event, text) {
  if (!text) return '';
  if (provider === 'copilot') return JSON.stringify({ additionalContext: text });
  return JSON.stringify({ hookSpecificOutput: { hookEventName: event, additionalContext: text } });
}

function designContext(stdout) {
  if (!stdout) return '';
  const parsed = JSON.parse(stdout);
  return parsed.additionalContext || parsed.additional_context || parsed.hookSpecificOutput?.additionalContext || '';
}

export async function handleEvent(raw, options, dependencies = {}) {
  const event = normalizeEvent(raw, options);
  const root = (dependencies.repositoryRoot || repositoryRoot)(event.cwd);
  if (event.kind === 'start' || event.kind === 'prompt') {
    (dependencies.startRequest || startRequest)(root, { session: event.session, event: event.kind, provider: event.provider, prompt: event.prompt });
    // Copilot userPromptSubmitted output is ignored. Its state update still
    // expires the preceding arm; do not depend on context from that event.
    return { stdout: event.kind === 'start' ? contextOutput(event.provider, 'SessionStart', `Nova verification session: ${event.session}. For implementation, declare intent before running verification: node eng/verify.mjs expect --session "${event.session}" --profile <quick|push|pre-pr|pre-merge> --base <commit>. Read-only work needs no checkpoint. A new user request expires the previous checkpoint.`) : '' };
  }
  if (event.kind === 'stop') {
    const decision = (dependencies.completionDecision || completionDecision)(root, { session: event.session, stopHookActive: event.stop_hook_active, readOnly: event.readOnly });
    // VS Code nests Stop output; Codex and Copilot agentStop use the top
    // level. Design heuristics never create another completion block.
    const block = { decision: 'block', reason: decision.reason };
    return decision.decision === 'block' ? { stdout: JSON.stringify(event.provider === 'vscode' ? { hookSpecificOutput: { hookEventName: 'Stop', ...block } } : block) } : { stdout: '', stderr: decision.verified ? '' : `Nova: ${decision.reason}` };
  }
  const { runHook, resolveTargetFiles, writeAuditLog } = dependencies.design || await import('../.agents/skills/impeccable/scripts/hook-lib.mjs');
  const targets = resolveTargetFiles(event, root);
  const notes = [];
  const once = key => (dependencies.rememberHint || rememberHint)(root, event.session, key);
  if (targets.some(file => /\.razor(?:\.cs|\.js)?$/i.test(file)) && once('ui-ownership')) notes.push('For recoverable or asynchronous UI behavior, map the owner, transition, visible effect, and proving test before extending the flow. Use add-blazor-ui; validate correction and stale completion paths as well as the success path.');
  if (targets.some(file => /(?:Endpoints|Service)\.cs$/i.test(file)) && once('boundary-tests')) notes.push('For HTTP or stateful service changes, pair the changed contract with its boundary test. Preserve tenant/admin semantics and inspect sibling entry points when closing a behavioral finding; use nova-testing for the independent review brief.');
  const result = await runHook({ stdinJson: JSON.stringify({ ...event, cwd: root }), cwd: root, env: { ...process.env, IMPECCABLE_HOOK_HARNESS: event.provider === 'copilot' ? 'github' : 'codex' } });
  writeAuditLog?.(process.env, result.audit, root);
  const design = designContext(result.stdout);
  if (design) notes.push(design);
  return { stdout: contextOutput(event.provider, 'PostToolUse', notes.join('\n\n')) };
}

async function main() {
  const args = process.argv.slice(2);
  const option = name => args[args.indexOf(name) + 1];
  try {
    let input = '';
    for await (const chunk of process.stdin) input += chunk;
    process.env.IMPECCABLE_HOOK_HARNESS = option('--provider') === 'copilot' ? 'github' : 'codex';
    const raw = JSON.parse(input);
    const result = await handleEvent(raw, { provider: option('--provider'), event: args.includes('--event') ? option('--event') : undefined });
    if (process.env.NOVA_HOOK_TRACE) {
      const trace = resolve(process.env.NOVA_HOOK_TRACE);
      mkdirSync(dirname(trace), { recursive: true });
      appendFileSync(trace, JSON.stringify({ at: new Date().toISOString(), provider: option('--provider'), event: option('--event'), nativeEvent: raw.hook_event_name || raw.hookEventName, session: raw.session_id || raw.sessionId, stopHookActive: raw.stop_hook_active === true || raw.stopHookActive === true, stdout: result.stdout, stderr: result.stderr }) + '\n');
    }
    if (result.stdout) process.stdout.write(result.stdout + '\n');
    if (result.stderr) process.stderr.write(result.stderr + '\n');
  } catch (error) {
    // Hook availability is advisory; failures never manufacture passing
    // evidence and never cause an unbounded continuation loop.
    process.stderr.write(`Nova hook unavailable; verification is unverified: ${error.message}\n`);
    if (process.env.NOVA_HOOK_TRACE) {
      try {
        const trace = resolve(process.env.NOVA_HOOK_TRACE); mkdirSync(dirname(trace), { recursive: true });
        appendFileSync(trace, JSON.stringify({ at: new Date().toISOString(), provider: option('--provider'), event: option('--event'), error: error.message }) + '\n');
      } catch { /* Trace failure cannot create a verification verdict. */ }
    }
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
