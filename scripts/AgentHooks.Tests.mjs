// Run with: node --test scripts/AgentHooks.Tests.mjs
// Temporary repositories exercise transport separately from the real detector.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { runHook, runStopHook, loadDetector } from '../.agents/skills/impeccable/scripts/hook-lib.mjs';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const skill = '.agents/skills/impeccable';
const manifests = ['.codex/hooks.json', '.github/hooks/impeccable.json'];
const nodeSupported = Number.parseInt(process.versions.node, 10) >= 22;
const unixShell = process.platform === 'win32' ? 'C:/Program Files/Git/bin/bash.exe' : '/bin/bash';

function execute(file, args, options = {}) {
  return spawnSync(file, args, { encoding: 'utf8', timeout: 30000, ...options });
}

function success(result) {
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr || result.stdout);
  return result.stdout;
}

function write(root, relative, contents) {
  const dest = path.join(root, relative);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, typeof contents === 'string' ? contents : `${JSON.stringify(contents, null, 2)}\n`);
  return dest;
}

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "nova agent hooks $ répo O'Brien "));
  t.after(() => {
    // Only remove this invocation's verified temporary directory.
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    assert.ok(path.basename(root).startsWith('nova agent hooks '));
    fs.rmSync(root, { recursive: true, force: true });
  });
  success(execute('git', ['init', '--quiet', root]));
  write(root, '.impeccable/config.json', JSON.parse(fs.readFileSync(path.join(repo, '.impeccable/config.json'), 'utf8')));
  // A nested npm project must still use Nova's repo-level detector config.
  write(root, 'nested app/package.json', '{}');
  return root;
}

function install(root, provider = '.agents') {
  fs.mkdirSync(path.join(root, skill), { recursive: true });
  fs.mkdirSync(path.join(root, '.github/skills/impeccable'), { recursive: true });
  return success(execute(process.execPath, [path.join(repo, provider, 'skills/impeccable/scripts/hook-admin.mjs'), 'on'], { cwd: root }));
}

function read(root, relative) {
  return JSON.parse(fs.readFileSync(path.join(root, relative), 'utf8'));
}

function codexEvent(cwd, file, session = 'codex-fixture') {
  return { hook_event_name: 'PostToolUse', turn_id: 'turn', session_id: session, cwd, tool_name: 'Edit', tool_input: { file_path: file } };
}

function copilotEvent(cwd, file) {
  return { timestamp: 1, cwd, toolName: 'edit', toolArgs: JSON.stringify({ path: file }) };
}

function env(root) {
  return { IMPECCABLE_HOOK_PROJECT_ROOT: root };
}

function finding(file, rule = 'broken-image') {
  return { antipattern: rule, name: rule, description: 'Fixture finding', severity: 'warning', file, line: 1, snippet: rule };
}

test('installer repairs both providers, preserves unrelated entries, and is idempotent', t => {
  const root = fixture(t);
  const unrelated = { type: 'command', command: 'echo unrelated' };
  write(root, manifests[0], { custom: true, hooks: { PostToolUse: [{ matcher: 'Read', hooks: [unrelated] }, { matcher: 'Edit', hooks: [{ type: 'command', commandWindows: 'node .agents/skills/impeccable/scripts/hook.mjs' }] }] } });
  write(root, manifests[1], { version: 1, hooks: { sessionStart: [{ type: 'command', bash: 'echo unrelated' }], postToolUse: [{ type: 'command', powershell: 'node .github/skills/impeccable/scripts/hook.mjs' }] } });
  install(root);
  const codex = read(root, manifests[0]);
  const copilot = read(root, manifests[1]);
  assert.equal(codex.custom, true);
  assert.deepEqual(codex.hooks.PostToolUse[0].hooks, [unrelated]);
  assert.equal(codex.hooks.PostToolUse.length, 2);
  assert.equal(copilot.hooks.sessionStart[0].bash, 'echo unrelated');
  assert.equal(copilot.hooks.postToolUse.length, 1);
  const installed = manifests.map(p => fs.readFileSync(path.join(root, p), 'utf8'));
  install(root, '.github');
  assert.deepEqual(manifests.map(p => fs.readFileSync(path.join(root, p), 'utf8')), installed);
  // Compare owned entries with committed manifests: producer and output agree.
  assert.deepEqual(codex.hooks.PostToolUse[1], read(repo, manifests[0]).hooks.PostToolUse[0]);
  assert.deepEqual(codex.hooks.Stop, read(repo, manifests[0]).hooks.Stop);
  assert.deepEqual(copilot.hooks.postToolUse, read(repo, manifests[1]).hooks.postToolUse);
});

test('Razor routes to HTML, CSS/JS remain covered, and .razor.cs is excluded', async t => {
  const root = fixture(t);
  const nested = path.join(root, 'nested app');
  const calls = [];
  const detector = {
    detectHtml: file => { calls.push(['html', file]); return [finding(file)]; },
    detectText: (_text, file) => { calls.push(['text', file]); return [finding(file)]; },
  };
  for (const [filename, engine] of [['Panel.razor', 'html'], ['Panel.razor.css', 'text'], ['Panel.razor.js', 'text'], ['Panel.razor.cs', null]]) {
    write(root, `nested app/${filename}`, '@if (Visible) { <img> }');
    const previous = calls.length;
    const result = await runHook({ cwd: nested, env: env(root), stdinJson: codexEvent(nested, filename, filename), detector });
    assert.equal(result.exitCode, 0);
    if (engine) {
      assert.equal(calls[previous][0], engine);
      assert.equal(calls[previous][1], path.join(nested, filename));
      assert.match(JSON.parse(result.stdout).hookSpecificOutput.additionalContext, /broken-image/);
      assert.equal(result.audit.cwd, root);
    } else {
      assert.equal(calls.length, previous);
      assert.equal(result.audit.skipped, 'extension');
      assert.equal(result.stdout, '');
    }
  }
});

test('Copilot JSON arguments and raw patches keep their context envelope', async t => {
  const root = fixture(t);
  write(root, 'Panel.razor', '<img>');
  const detector = { detectText: () => [], detectHtml: file => [finding(file)] };
  for (const input of [copilotEvent(root, 'Panel.razor'), { cwd: root, toolName: 'apply_patch', toolArgs: '*** Begin Patch\n*** Update File: Panel.razor\n@@\n-<p>Text</p>\n+<img>\n*** End Patch' }]) {
    const result = await runHook({ cwd: root, env: env(root), stdinJson: input, detector });
    assert.equal(result.audit.harness, 'github');
    assert.equal(typeof JSON.parse(result.stdout).additionalContext, 'string');
    assert.equal(JSON.parse(result.stdout).hookSpecificOutput, undefined);
  }
  const codex = codexEvent(root, 'Panel.razor', 'raw-codex');
  codex.tool_name = 'apply_patch';
  codex.tool_input = { command: '*** Begin Patch\n*** Update File: Panel.razor\n@@\n-<p>Text</p>\n+<img>\n*** End Patch' };
  const result = await runHook({ cwd: root, env: env(root), stdinJson: codex, detector });
  assert.match(JSON.parse(result.stdout).hookSpecificOutput.additionalContext, /broken-image/);
});

test('nested Codex Stop scans deferred findings once and cannot loop', async t => {
  const root = fixture(t);
  const nested = path.join(root, 'nested app');
  write(root, 'nested app/Panel.razor', '<p>Text</p>');
  const detector = { detectText: () => [], detectHtml: file => [finding(file, 'overused-font')] };
  const result = await runHook({ cwd: nested, env: env(root), stdinJson: codexEvent(nested, 'Panel.razor'), detector });
  assert.equal(result.audit.cwd, root);
  const stop = { hook_event_name: 'Stop', turn_id: 'turn', session_id: 'codex-fixture', cwd: nested };
  const first = await runStopHook({ cwd: nested, env: env(root), stdinJson: stop, detector });
  assert.equal(JSON.parse(first.stdout).decision, 'block');
  assert.match(JSON.parse(first.stdout).reason, /overused-font/);
  const repeated = await runStopHook({ cwd: nested, env: env(root), stdinJson: stop, detector });
  assert.equal(repeated.stdout, '');
  const active = await runStopHook({ cwd: nested, env: env(root), stdinJson: { ...stop, stop_hook_active: true }, detector: { detectText: () => { assert.fail('reentrant stop scanned'); } } });
  assert.equal(active.audit.skipped, 'stop-hook-active');
  assert.equal(active.stdout, '');
});

test('malformed input and detector failure fail open without clean acknowledgements', async t => {
  const root = fixture(t);
  const malformed = await runHook({ cwd: root, stdinJson: '{' });
  assert.equal(malformed.exitCode, 0);
  assert.equal(malformed.audit.skipped, 'stdin-malformed');
  assert.equal(malformed.stdout, '');
  write(root, 'Panel.razor', '<p>Text</p>');
  const failed = await runHook({ cwd: root, env: env(root), stdinJson: codexEvent(root, 'Panel.razor'), detector: { detectText: () => [], detectHtml: () => { throw Error('fixture detector unavailable'); } } });
  assert.equal(failed.exitCode, 0);
  assert.equal(failed.stdout, '');
  assert.notEqual(failed.audit.skipped, 'clean');
  assert.ok(failed.audit.detectorThrew || failed.audit.error || failed.audit.skipped === 'detector-error', JSON.stringify(failed.audit));
});

test('real bundled detector finds broken Razor markup and acknowledges a clean fragment', async t => {
  const root = fixture(t);
  const detector = await loadDetector();
  assert.equal(typeof detector.detectHtml, 'function');
  write(root, 'Broken.razor', '@page "/fixture"\n@if (Visible) {\n<img alt="Fixture" />\n}\n@code { private bool Visible = true; }');
  const broken = await runHook({ cwd: root, env: env(root), stdinJson: codexEvent(root, 'Broken.razor', 'real-broken'), detector });
  assert.match(JSON.parse(broken.stdout).hookSpecificOutput.additionalContext, /broken-image/);
  write(root, 'Clean.razor', '@if (Visible) { <p>Ready</p> }\n@code { private bool Visible = true; }');
  const clean = await runHook({ cwd: root, env: env(root), stdinJson: codexEvent(root, 'Clean.razor', 'real-clean'), detector });
  assert.match(JSON.parse(clean.stdout).hookSpecificOutput.additionalContext, /clean/i);
  assert.equal(clean.audit.skipped, undefined);
});

// The stub preserves bytes on stdin; transport success cannot prove a scan ran.
const echoHook = `let input = ''; for await (const chunk of process.stdin) input += chunk; process.stdout.write(JSON.stringify({ input, cwd: process.cwd(), root: process.env.IMPECCABLE_HOOK_PROJECT_ROOT }));`;

function shellRun(kind, entry, cwd, input, extraEnv = {}) {
  const options = { cwd, input, env: { ...process.env, ...extraEnv } };
  if (kind === 'bash') return execute(unixShell, ['-c', entry.bash || entry.command], options);
  if (kind === 'codex-windows') return execute('cmd.exe', ['/d', '/s', '/c', entry.commandWindows], { ...options, windowsVerbatimArguments: true });
  return execute('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', entry.powershell], options);
}

test('generated commands preserve stdin/cwd from root and nested directories with spaces', async t => {
  const root = fixture(t);
  install(root);
  write(root, `${skill}/scripts/hook.mjs`, echoHook);
  write(root, '.github/skills/impeccable/scripts/hook.mjs', echoHook);
  const codex = read(root, manifests[0]).hooks.PostToolUse[0].hooks[0];
  const copilot = read(root, manifests[1]).hooks.postToolUse[0];
  const kinds = [['bash', codex], ['bash', copilot], ['codex-windows', codex], ['powershell', copilot]];
  for (const [index, [kind, entry]] of kinds.entries()) {
    await t.test(`${kind} provider ${index}`, child => {
      if (kind !== 'bash' && process.platform !== 'win32') return child.skip('Windows host transport requires Windows.');
      if (!nodeSupported) return child.skip('Node 22+ unavailable; dispatch unverified. The separate runtime guard test covers skipped-scan reporting.');
      if (kind === 'bash' && process.platform === 'win32' && !fs.existsSync(unixShell)) return child.skip('Git Bash is unavailable; Unix transport unverified on this host.');
      for (const cwd of [root, path.join(root, 'nested app')]) {
        const input = `${JSON.stringify({ ...codexEvent(cwd, 'Panel.razor'), sentinel: "résumé ☘ $value `ticks` 'quotes'" })}\n`;
        const result = shellRun(kind, entry, cwd, input);
        success(result);
        const output = JSON.parse(result.stdout);
        assert.equal(output.input, input);
        assert.equal(path.resolve(output.cwd), cwd);
        assert.equal(path.resolve(output.root), root);
      }
    });
  }
});

test('real hook subprocesses deliver findings and audit malformed input', t => {
  if (!nodeSupported) return t.skip('Node 22+ launcher unavailable; API detector tests still run, dispatch is unverified.');
  const root = fixture(t);
  install(root);
  for (const provider of ['.agents', '.github']) {
    fs.cpSync(path.join(repo, provider, 'skills/impeccable/scripts'), path.join(root, provider, 'skills/impeccable/scripts'), { recursive: true });
  }
  write(root, 'nested app/Panel.razor', '@if (Visible) { <img alt="Fixture" /> }');
  const cwd = path.join(root, 'nested app');
  const config = read(root, '.impeccable/config.json');
  config.hook.auditLog = 'hook-audit.ndjson';
  write(root, '.impeccable/config.json', config);
  const auditPath = path.join(root, 'hook-audit.ndjson');
  const extraEnv = { IMPECCABLE_HOOK_LOG: '', IMPECCABLE_HOOK_DISABLED: '', IMPECCABLE_HOOK_DEPTH: '', CLAUDE_HOOK_DEPTH: '' };
  const codex = read(root, manifests[0]).hooks.PostToolUse[0].hooks[0];
  const copilot = read(root, manifests[1]).hooks.postToolUse[0];
  const codexRun = shellRun(process.platform === 'win32' ? 'codex-windows' : 'bash', codex, cwd, JSON.stringify(codexEvent(cwd, 'Panel.razor')), extraEnv);
  assert.match(JSON.parse(success(codexRun)).hookSpecificOutput.additionalContext, /broken-image/);
  const copilotRun = shellRun(process.platform === 'win32' ? 'powershell' : 'bash', copilot, cwd, JSON.stringify(copilotEvent(cwd, 'Panel.razor')), extraEnv);
  assert.match(JSON.parse(success(copilotRun)).additionalContext, /broken-image/);
  const malformed = shellRun(process.platform === 'win32' ? 'powershell' : 'bash', copilot, cwd, '{', extraEnv);
  assert.equal(success(malformed), '');
  const entries = fs.readFileSync(auditPath, 'utf8').trim().split(/\r?\n/).map(JSON.parse);
  assert.ok(entries.some(entry => entry.harness === 'codex' && entry.cwd === root));
  assert.ok(entries.some(entry => entry.harness === 'github' && entry.cwd === root));
  assert.equal(entries.at(-1).skipped, 'stdin-malformed');
  assert.equal(fs.existsSync(path.join(cwd, 'hook-audit.ndjson')), false);
});

test('Windows launchers preserve Unicode paths and stdin under OEM code page 437', t => {
  if (process.platform !== 'win32') return t.skip('Windows native-output decoding requires Windows.');
  if (!nodeSupported) return t.skip('Node 22+ unavailable; launcher dispatch unverified.');
  const root = fixture(t);
  install(root);
  write(root, `${skill}/scripts/hook.mjs`, echoHook);
  write(root, '.github/skills/impeccable/scripts/hook.mjs', echoHook);
  const codex = read(root, manifests[0]).hooks.PostToolUse[0].hooks[0];
  const copilot = read(root, manifests[1]).hooks.postToolUse[0];
  // Simulate legacy host decoding inside each short-lived PowerShell process.
  // The generated command then runs unchanged; parent settings are untouched.
  const legacyEncoding = '[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(437); ';
  const codexCommandPrefix = '-Command "& { ';
  assert.ok(codex.commandWindows.includes(codexCommandPrefix));
  codex.commandWindows = codex.commandWindows.replace(codexCommandPrefix, codexCommandPrefix + legacyEncoding);
  copilot.powershell = legacyEncoding + copilot.powershell;
  const cwd = path.join(root, 'nested app');
  const input = `${JSON.stringify({ ...codexEvent(cwd, 'Panel.razor'), sentinel: 'résumé ☘' })}\n`;
  for (const [kind, entry] of [['codex-windows', codex], ['powershell', copilot]]) {
    const output = JSON.parse(success(shellRun(kind, entry, cwd, input)));
    assert.equal(path.resolve(output.root), root);
    assert.equal(output.input, input);
    assert.equal(path.resolve(output.cwd), cwd);
  }
});

test('unsupported runtime guard reports a skipped scan instead of claiming clean', async t => {
  const root = fixture(t);
  install(root);
  write(root, `${skill}/scripts/hook.mjs`, 'throw Error("unsupported runtime must not dispatch");');
  // A private PATH containing git and a deliberately failing node is sufficient
  // to exercise the actual Unix launcher without installing an older runtime.
  if (process.platform === 'win32' && !fs.existsSync(unixShell)) return t.skip('Git Bash unavailable; runtime guard transport unverified.');
  const bin = path.join(root, 'fake bin');
  const gitPath = success(execute(process.platform === 'win32' ? 'where.exe' : 'which', ['git'])).trim().split(/\r?\n/)[0].replaceAll('\\', '/');
  write(root, 'fake bin/node', '#!/bin/sh\nexit 1\n');
  write(root, 'fake bin/git', `#!/bin/sh\nexec '${gitPath.replaceAll("'", "'\\''")}' "$@"\n`);
  fs.chmodSync(path.join(bin, 'node'), 0o755);
  fs.chmodSync(path.join(bin, 'git'), 0o755);
  const entry = read(root, manifests[0]).hooks.PostToolUse[0].hooks[0];
  const result = shellRun('bash', entry, root, '{}', { PATH: bin });
  success(result);
  assert.equal(result.stdout, '');
  assert.match(result.stderr, /Scan skipped: Node 22\+ unavailable/);
});
