import { createHash, randomUUID } from 'node:crypto';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, unlinkSync } from 'node:fs';
import { join } from 'node:path';
import { git, repositoryRoot, resolveCommit, changedPaths } from './git.mjs';
import { evidenceStatus, writeJson } from './evidence.mjs';
import { PROFILES } from './policy.mjs';

// Hook state is local evidence, never an authorization boundary. Each native
// request owns one continuation allowance even if expect is called again.
function sessionFile(root, session) {
  if (typeof session !== 'string' || !session.trim() || session.length > 512) throw new Error('A native hook session ID is required.');
  const key = createHash('sha256').update(session).digest('hex');
  return join(root, 'artifacts/verification/checkpoints', key + '.json');
}

function binding(root) {
  return { root: repositoryRoot(root), branch: git(root, ['branch', '--show-current']).trim() || resolveCommit(root, 'HEAD') };
}

function update(root, session, change) {
  root = repositoryRoot(root);
  const file = sessionFile(root, session);
  mkdirSync(join(root, 'artifacts/verification/checkpoints'), { recursive: true });
  const lock = file + '.lock';
  let handle;
  try { handle = openSync(lock, 'wx'); }
  catch (error) { throw new Error(`Checkpoint state is busy; retry after the other hook finishes (${error.code}).`); }
  try {
    const previous = existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null;
    const result = change(previous, root);
    if (result.state) writeJson(file, result.state);
    return result.value;
  } finally { closeSync(handle); unlinkSync(lock); }
}

export function startRequest(root, { session, event = 'prompt', provider, prompt }) {
  root = repositoryRoot(root);
  const epoch = randomUUID();
  const epochFile = sessionFile(root, session) + '.epoch';
  const previousEpoch = existsSync(epochFile) ? JSON.parse(readFileSync(epochFile, 'utf8')).epoch : null;
  // Expire the old arm before attempting its state lock. A prompt arriving
  // during a slow Stop check must not inherit the preceding request's arm.
  writeJson(sessionFile(root, session) + '.epoch', { epoch });
  return update(root, session, (previous, canonicalRoot) => {
    if (JSON.parse(readFileSync(sessionFile(root, session) + '.epoch')).epoch !== epoch) return { value: { session, superseded: true } };
    const currentBinding = binding(canonicalRoot);
    // Copilot delivers our corrective reason through userPromptSubmitted too.
    // Every prompt still expires intent. Only its exact recorded hash retains
    // the spent allowance; no prompt text or old checkpoint is carried over.
    const correctiveEcho = event === 'prompt' && provider === 'copilot' && typeof prompt === 'string'
      && previous?.continuations === 1 && previous.epoch === previousEpoch
      && previous.binding.root === currentBinding.root && previous.binding.branch === currentBinding.branch
      && previous.correctiveReasonHash === createHash('sha256').update(prompt).digest('hex');
    const state = { version: 1, session, epoch, binding: currentBinding, startedAt: new Date().toISOString(), event, continuations: correctiveEcho ? 1 : 0, hints: [] };
    if (correctiveEcho) state.correctiveReasonHash = previous.correctiveReasonHash;
    return { state, value: { session, epoch: state.epoch } };
  });
}

function requireRequest(state, root) {
  if (!state?.epoch) throw new Error('No native request is registered for this session. Enable the repository hooks and start a new request before declaring verification intent.');
  if (JSON.parse(readFileSync(sessionFile(root, state.session) + '.epoch')).epoch !== state.epoch) throw new Error('A newer native request expired this checkpoint. Retry after its hook initializes.');
  const current = binding(root);
  if (state.binding.root !== current.root || state.binding.branch !== current.branch) throw new Error('The checkout or branch changed. Start a new request before declaring verification intent.');
}

export function expectCheckpoint(root, { session, profile, base }) {
  if (!PROFILES.has(profile)) throw new Error(`Unknown verification profile: ${profile}`);
  return update(root, session, (state, canonicalRoot) => {
    requireRequest(state, canonicalRoot);
    const checkpoint = { id: randomUUID(), epoch: state.epoch, profile, base: resolveCommit(canonicalRoot, base), after: new Date().toISOString() };
    delete state.deferred;
    state.checkpoint = checkpoint;
    return { state, value: { session, ...checkpoint, continuationsRemaining: Math.max(0, 1 - state.continuations) } };
  });
}

export function deferCheckpoint(root, { session, reason }) {
  if (typeof reason !== 'string' || !reason.trim()) throw new Error('Explain the user input or external blocker in --reason.');
  return update(root, session, (state, canonicalRoot) => {
    requireRequest(state, canonicalRoot);
    delete state.checkpoint;
    state.deferred = { reason: reason.trim(), at: new Date().toISOString() };
    return { state, value: { deferred: true, session, reason: state.deferred.reason, verdict: 'unverified' } };
  });
}

export function rememberHint(root, session, key) {
  return update(root, session, state => {
    if (!state || state.hints.includes(key)) return { value: false };
    state.hints.push(key);
    return { state, value: true };
  });
}

export function completionDecision(root, { session, stopHookActive = false, readOnly = false }, dependencies = {}) {
  if (stopHookActive || readOnly) return { decision: 'allow', reason: 'Native continuation guard or read-only agent.' };
  return update(root, session, (state, canonicalRoot) => {
    if (!state?.checkpoint || state.deferred) return { value: { decision: 'allow', reason: 'No implementation checkpoint is armed; verification remains unverified.' } };
    try { requireRequest(state, canonicalRoot); }
    catch { delete state.checkpoint; return { state, value: { decision: 'allow', reason: 'Checkpoint expired after checkout or branch change; verification remains unverified.' } }; }
    const checkpoint = state.checkpoint;
    if (checkpoint.epoch !== state.epoch) return { value: { decision: 'allow', reason: 'Checkpoint belongs to an earlier request.' } };
    // Always inspect the entire current diff, including untracked files. The
    // design detector's per-edit caps and caches cannot establish completeness.
    const paths = (dependencies.changedPaths || changedPaths)(canonicalRoot, checkpoint.base);
    const status = (dependencies.evidenceStatus || evidenceStatus)(canonicalRoot, checkpoint.profile, { after: checkpoint.after, base: checkpoint.base });
    try { requireRequest(state, canonicalRoot); }
    catch { delete state.checkpoint; return { state, value: { decision: 'allow', reason: 'A new request or checkout change expired this checkpoint while verification was checked.' } }; }
    if (status.current && status.verdict === 'passed') return { value: { decision: 'allow', verified: true, profile: checkpoint.profile, changedFiles: paths.length } };
    if (state.continuations >= 1) return { value: { decision: 'allow', reason: `Verification is ${status.verdict}; the request's single corrective continuation has already been used. Report the remaining gap.`, verified: false } };
    state.continuations = 1; // Persist before returning a native block response.
    const reason = `Verification for the armed ${checkpoint.profile} checkpoint is ${status.verdict} (${paths.length} changed files). Run node eng/verify.mjs run --profile ${checkpoint.profile} --base ${checkpoint.base}${checkpoint.profile === 'quick' ? ' --suite <affected-suite>' : ''}, then inspect its result. If waiting on the user or an external blocker, record node eng/verify.mjs defer --session ${session} --reason "<specific blocker>" and report the gap. Do not claim a pass without current evidence. This is the only corrective continuation for this request.`;
    state.correctiveReasonHash = createHash('sha256').update(reason).digest('hex');
    return { state, value: { decision: 'block', reason, verified: false } };
  });
}
