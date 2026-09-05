import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { git } from '../lib/git.mjs';
import { createDesignRun, assertWritableArtifact, safeRunId } from '../../.agents/skills/impeccable/scripts/lib/design-run-paths.mjs';
import { designTool } from '../../.agents/skills/impeccable/scripts/design-run.mjs';
import { encodePng } from '../../.agents/skills/impeccable/scripts/lib/png.mjs';
import { writeSnapshot, readLatestSnapshot, closeSnapshot } from '../../.agents/skills/impeccable/scripts/critique-storage.mjs';

function fixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'nova-design-evidence-'));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  git(root, ['init', '-b', 'main']);
  for (const kind of ['build', 'review', 'mocks']) mkdirSync(join(root, '.impeccable', kind), { recursive: true });
  writeFileSync(join(root, '.impeccable/build/spec.json'), '{"historical":true}\n');
  const comp = join(root, '.impeccable/mocks/approved.png');
  writeFileSync(comp, encodePng({ width: 2, height: 2, data: new Uint8Array(16).fill(255) }));
  return { root, comp };
}

test('design runs allocate fresh directories without overwriting historical inputs', t => {
  const { root, comp } = fixture(t);
  const source = { head: 'test', fingerprint: 'test-source' };
  const first = createDesignRun(root, source), second = createDesignRun(root, source);
  assert.notEqual(first.id, second.id);
  assert.ok(existsSync(join(root, first.directory, 'source-start.json')));
  assert.deepEqual(JSON.parse(readFileSync(join(root, first.directory, 'source-start.json'))).source, source);
  const original = readFileSync(comp);
  assert.throws(() => assertWritableArtifact(comp, root), /read-only/);
  assert.throws(() => assertWritableArtifact('.impeccable/build/spec.json', root), /read-only/);
  assert.equal(assertWritableArtifact(join(root, first.directory, 'build/spec.json'), root), join(root, first.directory, 'build/spec.json'));
  assert.deepEqual(readFileSync(comp), original);
  assert.throws(() => safeRunId('../escape'), /Invalid/);
  assert.throws(() => designTool('../../other.mjs'), /known Impeccable/);
});

test('existing comp tool reads an approved historical input and writes only the selected new run', t => {
  const { root, comp } = fixture(t);
  const run = createDesignRun(root, { head: 'test' });
  const original = readFileSync(comp), oldSpec = readFileSync(join(root, '.impeccable/build/spec.json'));
  const runner = fileURLToPath(new URL('../../.agents/skills/impeccable/scripts/design-run.mjs', import.meta.url));
  const result = spawnSync(process.execPath, [runner, 'exec', run.id, 'comp-spec.mjs', '--comp', comp, '--grid'], { cwd: root, encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, result.stderr || result.stdout);
  assert.ok(existsSync(join(root, run.directory, 'build/comp-grid.png')));
  assert.deepEqual(readFileSync(comp), original);
  assert.deepEqual(readFileSync(join(root, '.impeccable/build/spec.json')), oldSpec);
  assert.equal(existsSync(join(root, '.impeccable/build/comp-grid.png')), false);
  const env = { ...process.env }; delete env.IMPECCABLE_RUN_ID;
  const direct = spawnSync(process.execPath, [designTool('comp-spec.mjs'), '--comp', comp, '--grid'], { cwd: root, env, encoding: 'utf8', windowsHide: true });
  assert.notEqual(direct.status, 0); assert.match(direct.stderr, /read-only/);
  assert.equal(existsSync(join(root, '.impeccable/build/comp-grid.png')), false);
});

test('critique reads historical snapshots but writes new snapshots only into its run', t => {
  const { root } = fixture(t);
  mkdirSync(join(root, '.impeccable/critique'));
  const historical = join(root, '.impeccable/critique/2026-01-01T00-00-00Z__sample.md');
  const body = '---\nslug: sample\n---\nHistorical review\n'; writeFileSync(historical, body);
  const previous = process.env.IMPECCABLE_RUN_ID; delete process.env.IMPECCABLE_RUN_ID;
  try {
    assert.equal(readLatestSnapshot('sample', { cwd: root }).body, body);
    assert.throws(() => closeSnapshot(historical, { cwd: root }), /read-only/);
    const run = createDesignRun(root, { head: 'test' });
    process.env.IMPECCABLE_RUN_ID = run.id;
    const saved = writeSnapshot({ slug: 'sample', meta: {}, body: 'Current review', cwd: root });
    assert.ok(saved.includes(run.id));
    assert.match(readLatestSnapshot('sample', { cwd: root }).body, /Current review/);
    assert.equal(readFileSync(historical, 'utf8'), body);
  } finally { if (previous === undefined) delete process.env.IMPECCABLE_RUN_ID; else process.env.IMPECCABLE_RUN_ID = previous; }
});
