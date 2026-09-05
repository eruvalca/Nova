import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync, renameSync, existsSync, readFileSync } from 'node:fs';
import { setTimeout as delay } from 'node:timers/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verificationPlan, assessReport, OPTIONAL_SCREENSHOTS, POLICY_VERSION } from '../lib/policy.mjs';
import { git, changedPaths, sourceIdentity } from '../lib/git.mjs';
import { evidenceStatus, writeJson } from '../lib/evidence.mjs';
import { runCommand, checkoutLock } from '../lib/process.mjs';
import { argumentsFor, prepareRun } from '../verify.mjs';

function repository(t) {
  const root = mkdtempSync(join(tmpdir(), 'nova engineering space '));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  git(root, ['init', '-q']); git(root, ['config', 'user.email', 'engineering@example.invalid']); git(root, ['config', 'user.name', 'Engineering test']);
  git(root, ['config', 'core.autocrlf', 'false']);
  writeFileSync(join(root, '.gitignore'), 'artifacts/\nignored/\n');
  writeFileSync(join(root, 'initial.cs'), 'initial\n');
  git(root, ['add', '.']); git(root, ['commit', '-qm', 'initial']);
  return root;
}
function report(tests = [{ name: 'Example.Test', status: 'passed' }]) {
  const summary = { tests: tests.length, passed: 0, failed: 0, pending: 0, skipped: 0, other: 0 };
  for (const value of tests) summary[value.status]++;
  return { reportFormat: 'CTRF', results: { tests, summary, extra: { suites: [{ errors: [] }] } } };
}

test('push selects conservative boundaries and full profiles never filter', () => {
  for (const path of ['Nova.Shared/Features/Query.cs', 'Nova/Features/Service.cs', 'Nova.UI/Views.csproj', 'Nova.Client/Pages/Auth.razor', 'Nova.UI/Shared/State/UiIdentityScope.cs', 'eng/verify.mjs', 'unknown.config', '.agents/skills/example/scripts/new.mjs', '.codex/hooks.json']) {
    assert.deepEqual(verificationPlan({ profile: 'push', paths: [path] }).suites, ['unit', 'integration', 'browser'], path);
  }
  assert.deepEqual(verificationPlan({ profile: 'push', paths: ['Nova.Client/Page.razor'] }).suites, ['unit', 'browser']);
  assert.deepEqual(verificationPlan({ profile: 'push', paths: ['docs/readme.md'] }).suites, ['unit']);
  assert(verificationPlan({ profile: 'push', paths: ['Nova/scss/_variables.scss'] }).checks.includes('contrast'));
  for (const profile of ['ci', 'pre-pr', 'pre-merge']) {
    assert.deepEqual(verificationPlan({ profile }).suites, ['unit', 'integration', 'browser']);
    assert.throws(() => verificationPlan({ profile, filters: ['Narrow'] }));
  }
  assert.throws(() => verificationPlan({ profile: 'quick' }));
  assert.deepEqual(verificationPlan({ profile: 'quick', suites: ['unit'], filters: ['Focused*'] }).checks, ['build', 'unit']);
});

test('HTTP client adapters select integration before the generic interactive boundary', () => {
  for (const path of ['Nova.Client/Services/Campaigns/HttpCampaignQueryService.cs', 'Nova.Client/Services/Players/HttpPlayerService.cs']) {
    assert.deepEqual(verificationPlan({ profile: 'push', paths: [path] }).suites, ['unit', 'integration', 'browser'], path);
  }
  assert.deepEqual(verificationPlan({ profile: 'push', paths: ['Nova.Client/Pages/Example.razor'] }).suites, ['unit', 'browser']);
});

test('selection includes committed, index, worktree, nonignored untracked and both rename sides', t => {
  const root = repository(t); const base = git(root, ['rev-parse', 'HEAD']).trim();
  writeFileSync(join(root, 'committed.cs'), 'committed'); git(root, ['add', '.']); git(root, ['commit', '-qm', 'next']);
  git(root, ['mv', 'initial.cs', 'renamed.cs']);
  writeFileSync(join(root, 'renamed.cs'), 'unstaged');
  writeFileSync(join(root, 'new.cs'), 'untracked');
  mkdirSync(join(root, 'ignored')); writeFileSync(join(root, 'ignored', 'skip.cs'), 'skip');
  assert.deepEqual(changedPaths(root, base), ['committed.cs', 'initial.cs', 'new.cs', 'renamed.cs']);
  assert.throws(() => changedPaths(root, 'definitely-missing-base'));
  const before = sourceIdentity(root).fingerprint;
  mkdirSync(join(root, 'artifacts')); writeFileSync(join(root, 'artifacts', 'manifest.json'), 'evidence');
  assert.equal(sourceIdentity(root).fingerprint, before);
  writeFileSync(join(root, 'new.cs'), 'changed'); assert.notEqual(sourceIdentity(root).fingerprint, before);
  writeFileSync(join(root, 'stage-only.cs'), 'original'); git(root, ['add', '.']); git(root, ['commit', '-qm', 'original']);
  writeFileSync(join(root, 'stage-only.cs'), 'staged'); git(root, ['add', 'stage-only.cs']);
  writeFileSync(join(root, 'stage-only.cs'), 'original');
  assert(changedPaths(root, 'HEAD').includes('stage-only.cs'));
});

test('reports reject failure, zero tests, malformed counts, cancellation and unexpected skips', () => {
  assert.equal(assessReport(report()).verdict, 'passed');
  for (const status of ['failed', 'pending', 'other', 'skipped']) assert.equal(assessReport(report([{ name: 'Test', status }])).verdict, 'failed');
  assert.equal(assessReport(report([])).verdict, 'failed');
  assert.throws(() => assessReport({}));
  const malformed = report(); malformed.results.summary.passed = 4; assert.throws(() => assessReport(malformed));
  const cleanup = report(); cleanup.results.extra.suites[0].errors.push({ message: 'cleanup failed' }); assert.equal(assessReport(cleanup).verdict, 'failed');
  const screenshots = report([{ name: 'Executed', status: 'passed' }, { name: [...OPTIONAL_SCREENSHOTS][0], status: 'skipped' }]);
  assert.equal(assessReport(screenshots, { suite: 'browser' }).verdict, 'passed');
  assert.equal(assessReport(screenshots, { suite: 'browser', screenshots: true }).verdict, 'failed');
  assert.equal(assessReport(screenshots, { suite: 'unit' }).verdict, 'failed');
});

test('current evidence requires one complete run with extant reports; edits and new failures invalidate', t => {
  const root = repository(t); const source = sourceIdentity(root);
  const directory = join(root, 'artifacts/verification/run'); const file = join(directory, 'manifest.json');
  const reportPath = join(directory, 'unit/results.json'); writeJson(reportPath, report());
  const record = { runId: 'run', profile: 'quick', policyVersion: POLICY_VERSION, base: source.head, source, finalSource: source,
    startedAt: '2026-01-01T00:00:00.000Z', endedAt: '2026-01-01T00:00:01.000Z', verdict: 'passed',
    plan: verificationPlan({ profile: 'quick', suites: ['unit'] }), steps: [{ name: 'build', exitCode: 0, verdict: 'passed' }, { name: 'unit', exitCode: 0, verdict: 'passed', reportPath }] };
  writeJson(file, record); assert.equal(evidenceStatus(root, 'quick').verdict, 'passed');
  assert.equal(evidenceStatus(root, 'pre-pr').verdict, 'missing');
  assert.equal(evidenceStatus(root, 'quick', { after: '2026-01-02' }).verdict, 'missing');
  writeFileSync(reportPath, '{'); assert.equal(evidenceStatus(root, 'quick').verdict, 'incomplete');
  writeJson(reportPath, report());
  record.steps[0].timedOut = true; writeJson(file, record); assert.equal(evidenceStatus(root, 'quick').verdict, 'incomplete');
  record.steps[0].timedOut = false; writeJson(file, record);
  record.steps[0].cleanupError = 'Owned process group did not exit'; writeJson(file, record);
  assert.equal(evidenceStatus(root, 'quick').verdict, 'incomplete');
  delete record.steps[0].cleanupError; writeJson(file, record);
  writeFileSync(join(root, 'initial.cs'), 'edited'); assert.equal(evidenceStatus(root, 'quick').verdict, 'stale');
  writeFileSync(join(root, 'initial.cs'), 'initial\n');
  writeJson(join(root, 'artifacts/verification/new/manifest.json'), { ...record, startedAt: '2026-01-02T00:00:00.000Z', verdict: 'failed' });
  assert.equal(evidenceStatus(root, 'quick').verdict, 'failed');
  writeFileSync(join(root, 'artifacts/verification/new/manifest.json'), '{');
  assert.equal(evidenceStatus(root, 'quick').verdict, 'incomplete');
});

test('checkout lock has bounded contention and releases for the next run', async t => {
  const root = repository(t); const unlock = await checkoutLock(root);
  await assert.rejects(checkoutLock(root, { waitMs: 0 }), /lock is held/);
  unlock(); const release = await checkoutLock(root, { waitMs: 0 }); release();
});

test('selection after waiting includes edits made by the previous checkout owner', async t => {
  const root = repository(t); const options = { profile: 'push', base: 'HEAD' };
  const unlock = await checkoutLock(root);
  assert.deepEqual(prepareRun(root, options).plan.suites, ['unit']);
  const waiting = checkoutLock(root).then(release => {
    try { return prepareRun(root, options); } finally { release(); }
  });
  mkdirSync(join(root, 'Nova.Shared')); writeFileSync(join(root, 'Nova.Shared/Contract.cs'), 'new input'); unlock();
  const prepared = await waiting;
  assert.deepEqual(prepared.plan.suites, ['unit', 'integration', 'browser']);
  assert.equal(prepared.source.fingerprint, sourceIdentity(root).fingerprint);
});

test('advancing the recorded base ref expires an otherwise unchanged run', t => {
  const root = repository(t); const source = sourceIdentity(root);
  git(root, ['update-ref', 'refs/remotes/origin/main', source.head]);
  const directory = join(root, 'artifacts/verification/run'); const reportPath = join(directory, 'unit/results.json');
  writeJson(reportPath, report());
  const record = { runId: 'run', profile: 'quick', policyVersion: POLICY_VERSION, base: source.head, baseReference: 'origin/main', source, finalSource: source,
    startedAt: '2026-01-01T00:00:00.000Z', endedAt: '2026-01-01T00:00:01.000Z', verdict: 'passed',
    plan: verificationPlan({ profile: 'quick', suites: ['unit'] }), steps: [{ name: 'build', exitCode: 0, verdict: 'passed' }, { name: 'unit', exitCode: 0, verdict: 'passed', reportPath }] };
  writeJson(join(directory, 'manifest.json'), record);
  assert.equal(evidenceStatus(root, 'quick').verdict, 'passed');
  const tree = git(root, ['rev-parse', 'HEAD^{tree}']).trim();
  const next = git(root, ['commit-tree', tree, '-p', source.head, '-m', 'new base identity']).trim();
  git(root, ['update-ref', 'refs/remotes/origin/main', next]);
  assert.equal(evidenceStatus(root, 'quick').verdict, 'stale');
});

test('child failures and timeouts stay nonpassing and retain diagnostics', async t => {
  const root = repository(t);
  const failed = await runCommand(process.execPath, ['-e', 'console.error("diagnostic");process.exit(3)'], { cwd: root, log: join(root, 'artifacts/failure.log') });
  assert.equal(failed.exitCode, 3); assert(existsSync(failed.log));
  const timedOut = await runCommand(process.execPath, ['-e', 'setInterval(()=>{},1000)'], { cwd: root, log: join(root, 'artifacts/timeout.log'), timeoutMs: 100 });
  assert(timedOut.timedOut); assert.notEqual(timedOut.exitCode, 0);
});

test('CLI rejects accidental partial full runs and unsupported pass-through options', () => {
  assert.throws(() => argumentsFor(['run', '--profile', 'ci', '--ignore-exit-code', '2']));
  assert.throws(() => argumentsFor(['status', '--profile', 'ci', '--suite', 'unit']));
  assert.throws(() => argumentsFor(['run', '--profile', 'quick', '--install-browser']));
  assert.equal(argumentsFor(['run', '--profile', 'quick', '--suite', 'unit', '--filter-class', '*Tests']).suite[0], 'unit');
});

test('POSIX cancellation waits for the owned group after its parent exits', { skip: process.platform === 'win32' ? 'Windows uses taskkill /T; POSIX process groups require a POSIX host.' : false }, async t => {
  const root = repository(t);
  const marker = join(root, 'grandchild.pid');
  const abort = new AbortController();
  let grandchildPid;
  t.after(() => {
    if (grandchildPid) {
      try { process.kill(grandchildPid, 'SIGKILL'); } catch (error) { if (error.code !== 'ESRCH') throw error; }
    }
  });
  const grandchild = `process.on('SIGTERM', () => {}); require('node:fs').writeFileSync(${JSON.stringify(marker)}, String(process.pid)); setInterval(() => {}, 1000);`;
  const parent = `require('node:child_process').spawn(process.execPath, ['-e', ${JSON.stringify(grandchild)}], { stdio: 'ignore' }).unref(); setInterval(() => {}, 1000);`;
  const running = runCommand(process.execPath, ['-e', parent], { cwd: root, log: join(root, 'artifacts/group.log'), signal: abort.signal, timeoutMs: 30000 });
  const deadline = Date.now() + 10000;
  while (!existsSync(marker) && Date.now() < deadline) await delay(25);
  if (!existsSync(marker)) {
    abort.abort();
    await running;
    assert.fail('The owned grandchild did not become ready.');
  }
  grandchildPid = Number(readFileSync(marker, 'utf8'));
  assert(grandchildPid > 0);
  process.kill(grandchildPid, 0);
  abort.abort();

  const result = await running;

  assert(result.cancelled);
  assert.equal(result.cleanupError, undefined);
  assert.throws(() => process.kill(grandchildPid, 0), { code: 'ESRCH' }, 'runCommand must not return while an owned descendant is still alive');
  grandchildPid = undefined;
});
