import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { git } from '../lib/git.mjs';
import { completionDecision, deferCheckpoint, expectCheckpoint, startRequest } from '../lib/checkpoints.mjs';
import { handleEvent, normalizeEvent } from '../hooks.mjs';
import { coLocatedStylesheets, expandScanTargets, matchConfiguredExtension, payload, readConfig, perEditTieringActive, parseApplyPatchPaths } from '../../.agents/skills/impeccable/scripts/hook-lib.mjs';

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'nova-hooks-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  git(root, ['init', '-b', 'main']);
  writeFileSync(join(root, '.gitignore'), 'artifacts/\n');
  writeFileSync(join(root, 'sample.txt'), 'original');
  git(root, ['add', '.']);
  git(root, ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'fixture']);
  return root;
}
const missing = { evidenceStatus: () => ({ current: false, verdict: 'missing' }) };

test('intent requires a native request; two sessions have isolated one-continuation allowances', t => {
  const root = fixture(t);
  assert.throws(() => expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' }), /No native request/);
  for (const session of ['a', 'b']) {
    startRequest(root, { session });
    expectCheckpoint(root, { session, profile: 'push', base: 'HEAD' });
  }
  assert.equal(completionDecision(root, { session: 'a' }, missing).decision, 'block');
  writeFileSync(join(root, 'sample.txt'), 'cosmetic source change');
  expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' });
  assert.equal(completionDecision(root, { session: 'a' }, missing).decision, 'allow');
  assert.equal(completionDecision(root, { session: 'b' }, missing).decision, 'block');
});

test('new prompt expires previous intent; defer never creates passing evidence', t => {
  const root = fixture(t);
  startRequest(root, { session: 'a' });
  expectCheckpoint(root, { session: 'a', profile: 'pre-pr', base: 'HEAD' });
  startRequest(root, { session: 'a', event: 'prompt' });
  assert.match(completionDecision(root, { session: 'a' }, missing).reason, /No implementation checkpoint/);
  expectCheckpoint(root, { session: 'a', profile: 'pre-pr', base: 'HEAD' });
  assert.equal(deferCheckpoint(root, { session: 'a', reason: 'Awaiting an external service' }).verdict, 'unverified');
  assert.equal(completionDecision(root, { session: 'a' }, missing).decision, 'allow');
});

test('native stop guard and read-only agents never consume the allowance', t => {
  const root = fixture(t);
  startRequest(root, { session: 'a' });
  expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' });
  for (const options of [{ stopHookActive: true }, { readOnly: true }]) assert.equal(completionDecision(root, { session: 'a', ...options }, missing).decision, 'allow');
  assert.equal(completionDecision(root, { session: 'a' }, missing).decision, 'block');
});

test('completion examines all Git changes and demands current matching-profile evidence', t => {
  const root = fixture(t);
  startRequest(root, { session: 'a' });
  const arm = expectCheckpoint(root, { session: 'a', profile: 'pre-merge', base: 'HEAD' });
  rmSync(join(root, 'sample.txt'));
  for (let i = 0; i < 25; i++) writeFileSync(join(root, `new-${i}.txt`), 'untracked');
  const result = completionDecision(root, { session: 'a' }, {
    evidenceStatus: (actualRoot, profile, options) => {
      assert.equal(actualRoot, root); assert.equal(profile, 'pre-merge'); assert.deepEqual(options, { after: arm.after, base: arm.base });
      return { current: true, verdict: 'passed' };
    },
  });
  assert.equal(result.verified, true); assert.equal(result.changedFiles, 26);
});

test('branch changes expire intent rather than applying another branch checkpoint', t => {
  const root = fixture(t);
  startRequest(root, { session: 'a' });
  expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' });
  git(root, ['switch', '-c', 'other']);
  const result = completionDecision(root, { session: 'a' }, missing);
  assert.equal(result.decision, 'allow'); assert.match(result.reason, /expired/);
  assert.throws(() => expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' }), /branch changed/);
});

test('a new prompt during the Stop read expires intent even while its state lock is busy', t => {
  const root = fixture(t);
  startRequest(root, { session: 'a' });
  expectCheckpoint(root, { session: 'a', profile: 'push', base: 'HEAD' });
  const result = completionDecision(root, { session: 'a' }, { evidenceStatus: () => {
    assert.throws(() => startRequest(root, { session: 'a' }), /busy/);
    return { current: false, verdict: 'stale' };
  } });
  assert.equal(result.decision, 'allow'); assert.match(result.reason, /expired/);
});

test('failed and stale runs request correction; a nested working directory keeps the same binding', t => {
  const root = fixture(t);
  const nested = join(root, 'nested'); mkdirSync(nested);
  for (const verdict of ['failed', 'stale']) {
    startRequest(root, { session: verdict });
    expectCheckpoint(nested, { session: verdict, profile: 'push', base: 'HEAD' });
    assert.equal(completionDecision(nested, { session: verdict }, { evidenceStatus: () => ({ current: verdict === 'failed', verdict }) }).decision, 'block');
  }
});

test('native provider payloads normalize patch forms and use their actual Stop schemas', async () => {
  const raw = { cwd: '/checkout', sessionId: 'copilot-123', toolName: 'apply_patch', toolArgs: JSON.stringify({ input: '*** Begin Patch\n*** Update File: View.razor\n*** End Patch' }) };
  const normalized = normalizeEvent(raw, { provider: 'copilot', event: 'edit' });
  assert.match(normalized.tool_input.command, /Update File/);
  assert.deepEqual(parseApplyPatchPaths('*** Update File: Old.razor\n*** Move to: New.razor', '/checkout').map(file => file.split(/[\\/]/).at(-1)), ['Old.razor', 'New.razor']);
  const dependencies = { repositoryRoot: cwd => cwd, completionDecision: () => ({ decision: 'block', reason: 'Run verification' }) };
  for (const provider of ['codex', 'copilot', 'vscode']) {
    const output = await handleEvent({ session_id: 'native-123', hook_event_name: 'Stop' }, { provider, event: 'stop' }, dependencies);
    const data = JSON.parse(output.stdout);
    if (provider === 'codex') assert.deepEqual(data, { decision: 'block', reason: 'Run verification' });
    else assert.deepEqual(data, { hookSpecificOutput: { hookEventName: 'Stop', decision: 'block', reason: 'Run verification' } });
  }
  const copilot = await handleEvent({ sessionId: 'native-123' }, { provider: 'copilot', event: 'stop' }, dependencies);
  assert.deepEqual(JSON.parse(copilot.stdout), { decision: 'block', reason: 'Run verification' });
  assert.throws(() => normalizeEvent({ cwd: '/checkout' }, { provider: 'copilot', event: 'stop' }), /session ID/);
});

test('post-edit adapters inject bounded hints and design context without a completion block', async () => {
  const dependencies = { repositoryRoot: cwd => cwd, rememberHint: () => true, design: { resolveTargetFiles: () => ['Page.razor'], runHook: async () => ({ stdout: JSON.stringify({ additionalContext: 'Design warning' }) }) } };
  for (const provider of ['codex', 'copilot', 'vscode']) {
    const result = await handleEvent({ cwd: '/checkout', sessionId: 'one' }, { provider, event: 'edit' }, dependencies);
    const data = JSON.parse(result.stdout);
    assert.equal(data.decision, undefined);
    assert.match(provider === 'copilot' ? data.additionalContext : data.hookSpecificOutput.additionalContext, /Design warning/);
  }
});

test('Razor config and co-located CSS participate in advisory scanning', t => {
  const root = fixture(t);
  const component = join(root, 'Card.razor');
  writeFileSync(component, '<div>Card</div>'); writeFileSync(component + '.css', '.card { color: red; }');
  mkdirSync(join(root, '.impeccable'));
  writeFileSync(join(root, '.impeccable/config.json'), JSON.stringify({ detector: { extensions: [{ ext: '.razor', engine: 'html' }] } }));
  assert.equal(matchConfiguredExtension(component, readConfig(root).extensions).engine, 'html');
  assert.ok(coLocatedStylesheets(component).includes(component + '.css'));
  assert.ok(expandScanTargets([component], root).includes(component + '.css'));
  assert.equal(perEditTieringActive({}, 'codex'), false);
  assert.equal(payload('Design concern', 'Stop', 'codex'), '');
});

test('native Copilot corrective echoes expire intent but retain the spent allowance through re-arming and edits', async t => {
  const root = fixture(t);
  const session = 'copilot-native-echo';
  await handleEvent({ sessionId: session, timestamp: 1, cwd: root, prompt: 'Implement a change' }, { provider: 'copilot', event: 'prompt' });
  let arm = expectCheckpoint(root, { session, profile: 'push', base: 'HEAD' });
  const first = completionDecision(root, { session }, missing);
  assert.equal(first.decision, 'block');
  for (const timestamp of [2, 3]) {
    await handleEvent({ sessionId: session, timestamp, cwd: root, prompt: first.reason }, { provider: 'copilot', event: 'prompt' });
    assert.match(completionDecision(root, { session, stopHookActive: false }, missing).reason, /No implementation checkpoint/);
    const next = expectCheckpoint(root, { session, profile: 'push', base: 'HEAD' });
    assert.notEqual(next.epoch, arm.epoch);
    assert.equal(next.continuationsRemaining, 0);
    writeFileSync(join(root, 'sample.txt'), 'cosmetic change ' + timestamp);
    assert.equal(completionDecision(root, { session, stopHookActive: false }, missing).decision, 'allow');
    arm = next;
  }
  await handleEvent({ sessionId: session, timestamp: 4, cwd: root, prompt: 'A genuinely new implementation request' }, { provider: 'copilot', event: 'prompt' });
  assert.match(completionDecision(root, { session }, missing).reason, /No implementation checkpoint/);
  assert.equal(expectCheckpoint(root, { session, profile: 'push', base: 'HEAD' }).continuationsRemaining, 1);
  assert.equal(completionDecision(root, { session }, missing).decision, 'block');
});

test('the corrective allowance hash is exact and cannot cross a provider, session or branch', t => {
  const root = fixture(t);
  for (const scenario of ['different-text', 'other-provider', 'other-session', 'other-branch']) {
    const session = 'hash-' + scenario;
    startRequest(root, { session });
    expectCheckpoint(root, { session, profile: 'push', base: 'HEAD' });
    const first = completionDecision(root, { session }, missing);
    if (scenario === 'other-branch') git(root, ['switch', '-c', 'hash-other']);
    const nextSession = scenario === 'other-session' ? session + '-new' : session;
    startRequest(root, { session: nextSession, provider: scenario === 'other-provider' ? 'codex' : 'copilot', prompt: first.reason + (scenario === 'different-text' ? ' Please explain.' : '') });
    assert.match(completionDecision(root, { session: nextSession }, missing).reason, /No implementation checkpoint/);
    assert.equal(expectCheckpoint(root, { session: nextSession, profile: 'push', base: 'HEAD' }).continuationsRemaining, 1);
  }
});
