import { spawn, spawnSync } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, openSync, closeSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs';
import { delimiter, dirname, join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export function commandFor(program, args = []) {
  if (process.platform === 'win32' && program === 'npm') {
    const directories = [dirname(process.execPath), ...(process.env.PATH || '').split(delimiter)];
    const cli = directories.map(dir => join(dir, 'node_modules/npm/bin/npm-cli.js')).find(existsSync);
    if (!cli) throw new Error('Cannot locate npm-cli.js beside Node or npm on PATH.');
    return { program: process.execPath, args: [cli, ...args] };
  }
  return { program, args };
}

export function version(program, args = ['--version']) {
  const command = commandFor(program, args);
  const result = spawnSync(command.program, command.args, { encoding: 'utf8', timeout: 10000, windowsHide: true });
  if (result.error || result.status !== 0) throw new Error('Prerequisite failed: ' + program + ' (' + (result.error?.message || result.stderr?.trim() || result.status) + ')');
  return result.stdout.trim();
}

// Only signal the process group created by this run. A direct child's exit does
// not prove that same-group descendants using separate stdio have terminated.
async function terminateOwnedProcess(pid) {
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore', timeout: 10000 });
    return;
  }
  const signalGroup = signal => {
    try { process.kill(-pid, signal); return true; }
    catch (error) { if (error.code === 'ESRCH') return false; throw error; }
  };
  if (!signalGroup('SIGTERM')) return;
  const gracefulDeadline = Date.now() + 5000;
  while (true) {
    if (!signalGroup(0)) return;
    if (Date.now() >= gracefulDeadline) break;
    await delay(25);
  }
  if (!signalGroup('SIGKILL')) return;
  const killedDeadline = Date.now() + 5000;
  while (signalGroup(0)) {
    if (Date.now() >= killedDeadline) throw new Error('Owned process group did not exit after SIGKILL: ' + pid);
    await delay(25);
  }
}

export async function runCommand(program, args, { cwd, log, timeoutMs = 900000, env = {}, signal } = {}) {
  const command = commandFor(program, args);
  const startedAt = new Date().toISOString();
  mkdirSync(dirname(log), { recursive: true });
  const output = createWriteStream(log);
  const child = spawn(command.program, command.args, {
    cwd, env: { ...process.env, ...env }, shell: false, windowsHide: true,
    detached: process.platform !== 'win32', stdio: ['ignore', 'pipe', 'pipe'],
  });
  let timedOut = false;
  let cancelled = false;
  let termination;
  const kill = () => {
    if (!child.pid || termination) return;
    termination = terminateOwnedProcess(child.pid).then(() => undefined, error => error.message);
  };
  const timer = setTimeout(() => { timedOut = true; kill(); }, timeoutMs);
  const abort = () => { cancelled = true; kill(); };
  signal?.addEventListener('abort', abort, { once: true });
  if (signal?.aborted) abort();
  child.stdout.on('data', data => { output.write(data); process.stdout.write(data); });
  child.stderr.on('data', data => { output.write(data); process.stderr.write(data); });
  const result = await new Promise(resolveResult => {
    child.on('error', error => resolveResult({ exitCode: null, error: error.message }));
    child.on('close', (exitCode, exitSignal) => resolveResult({ exitCode, signal: exitSignal }));
  });
  clearTimeout(timer);
  signal?.removeEventListener('abort', abort);
  // Keep cancellation/timeout cleanup alive even after the direct child closes;
  // callers must not release their checkout lock while its descendants run.
  const cleanupError = await termination;
  if (cleanupError) output.write('Process cleanup failed: ' + cleanupError + '\n');
  await new Promise(done => output.end(done));
  return { command: [program, ...args], cwd, startedAt, endedAt: new Date().toISOString(), ...result, timedOut, cancelled, ...(cleanupError ? { cleanupError } : {}), log };
}

// Cooperative checkout lock. A crashed owner leaves a diagnostic lock requiring
// inspection; do not steal it based only on a recycled PID or a heartbeat timeout.
export async function checkoutLock(root, { waitMs = 600000, signal } = {}) {
  const file = resolve(root, 'artifacts/verification/checkout.lock');
  mkdirSync(dirname(file), { recursive: true });
  const deadline = Date.now() + waitMs;
  while (true) {
    try {
      const descriptor = openSync(file, 'wx');
      writeFileSync(descriptor, JSON.stringify({ pid: process.pid, root, startedAt: new Date().toISOString() }));
      return () => { closeSync(descriptor); unlinkSync(file); };
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
      if (Date.now() >= deadline) throw new Error('Checkout verification lock is held: ' + file + '\n' + readFileSync(file, 'utf8') + '\nInspect the owner and its child build processes before removing a stale lock.');
      await delay(500, undefined, { signal });
    }
  }
}
