#!/usr/bin/env node
import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { randomUUID } from 'node:crypto';
import { parseArgs } from 'node:util';
import { repositoryRoot, resolveCommit, changedPaths, sourceIdentity } from './lib/git.mjs';
import { verificationPlan, assessReport, POLICY_VERSION, PROFILES, SUITES } from './lib/policy.mjs';
import { checkoutLock, runCommand, version } from './lib/process.mjs';
import { writeJson, readJson, evidenceStatus } from './lib/evidence.mjs';

export function argumentsFor(args) {
  const { values, positionals } = parseArgs({ args, allowPositionals: true, strict: true, options: {
    profile: { type: 'string' }, base: { type: 'string' }, suite: { type: 'string', multiple: true },
    'filter-class': { type: 'string', multiple: true }, 'install-browser': { type: 'boolean' },
    json: { type: 'boolean' }, session: { type: 'string' }, reason: { type: 'string' }, help: { type: 'boolean' },
  } });
  if (values.help) return { action: 'help' };
  const action = positionals[0];
  if (positionals.length !== 1 || !['plan', 'run', 'status', 'expect', 'defer'].includes(action)) throw new Error('Use plan, run, status, expect, or defer. See --help.');
  const allowed = {
    plan: ['profile', 'base', 'suite', 'filter-class', 'json'],
    run: ['profile', 'base', 'suite', 'filter-class', 'install-browser'],
    status: ['profile', 'base', 'json'],
    expect: ['profile', 'base', 'session'], defer: ['session', 'reason'],
  }[action];
  for (const key of Object.keys(values)) if (!allowed.includes(key)) throw new Error('--' + key + ' is not supported by ' + action + '.');
  if (action !== 'defer' && !PROFILES.has(values.profile)) throw new Error('Specify a valid --profile.');
  if (action === 'run' && values['install-browser'] && values.profile !== 'ci') throw new Error('--install-browser is reserved for ci. Install local browsers using the documented Playwright script.');
  return { action, ...values };
}

export function prepareRun(root, options) {
  const source = sourceIdentity(root);
  const baseReference = options.base || (options.profile === 'quick' ? 'HEAD' : options.profile === 'push' ? undefined : 'origin/main');
  const base = resolveCommit(root, baseReference);
  const paths = options.profile === 'push' ? changedPaths(root, base) : [];
  const plan = verificationPlan({ profile: options.profile, paths, suites: options.suite || [], filters: options['filter-class'] || [] });
  plan.paths = paths;
  return { source, baseReference, base, plan };
}

async function execute(root, options) {
  const abort = new AbortController();
  const stop = () => abort.abort();
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
  let unlock;
  let manifest;
  let file;
  try {
    unlock = await checkoutLock(root, { signal: abort.signal });
    // Selection must happen after waiting: the previous owner may have edited
    // inputs while we were queued. End-of-run fingerprinting guards later edits.
    const { source, baseReference, base, plan } = prepareRun(root, options);
    const runId = new Date().toISOString().replaceAll(':', '-') + '-' + randomUUID().slice(0, 8);
    const directory = join(root, 'artifacts/verification', runId);
    mkdirSync(directory, { recursive: true });
    file = join(directory, 'manifest.json');
    manifest = {
      runId, profile: options.profile, policyVersion: POLICY_VERSION, base, baseReference, source,
      startedAt: new Date().toISOString(), verdict: 'running', plan, steps: [], changedPaths: plan.paths,
      checkoutSha: process.env.GITHUB_SHA || null,
      pullRequestHead: process.env.NOVA_PR_HEAD_SHA || null,
      pullRequestBase: process.env.NOVA_PR_BASE_SHA || null,
      versions: { node: process.version, platform: process.platform, architecture: process.arch },
      optionalScreenshotsEnabled: process.env.NOVA_A11Y_SCREENSHOTS === '1',
    };
    writeJson(file, manifest);
    if (process.env.TESTINGPLATFORM_EXITCODE_IGNORE) throw new Error('Unset TESTINGPLATFORM_EXITCODE_IGNORE: verification cannot accept suppressed test exits.');
    manifest.versions.dotnet = version('dotnet');
    manifest.versions.npm = version('npm');
    if (plan.suites.some(suite => suite !== 'unit')) manifest.versions.docker = version('docker', ['version', '--format', '{{.Client.Version}} / {{.Server.Version}}']);
    writeJson(file, manifest);
    const perform = async (name, program, args, { cwd = root, timeoutMs = 900000, env = {}, suite } = {}) => {
      if (abort.signal.aborted) throw new Error('Verification cancelled.');
      console.log('\n[' + name + '] ' + [program, ...args].join(' '));
      const step = { name, ...await runCommand(program, args, { cwd, log: join(directory, name + '.log'), timeoutMs, env, signal: abort.signal }) };
      if (suite) {
        step.reportPath = join(directory, suite, 'results.json');
        try { step.report = assessReport(readJson(step.reportPath), { suite, screenshots: manifest.optionalScreenshotsEnabled }); }
        catch (error) { step.reportError = error.message; }
      }
      step.verdict = step.exitCode === 0 && !step.timedOut && !step.cancelled && !step.cleanupError && (!suite || step.report?.verdict === 'passed') ? 'passed' : 'failed';
      manifest.steps.push(step); writeJson(file, manifest);
      if (step.verdict !== 'passed') throw new Error(name + ' failed; inspect ' + step.log + (step.reportError ? ': ' + step.reportError : ''));
    };
    for (const check of plan.checks) {
      if (check === 'engineering') await perform(check, 'node', ['eng/test.mjs']);
      else if (check === 'guidance') await perform(check, 'node', ['eng/guidance.mjs', 'check']);
      else if (check === 'build') {
        await perform(check, 'dotnet', ['build', 'Nova.slnx', '--nologo']);
        if (options['install-browser']) await perform('install-browser', 'pwsh', ['-NoProfile', '-File', 'Nova.Browser.Tests/bin/Debug/net10.0/playwright.ps1', 'install', '--with-deps', 'chromium'], { timeoutMs: 600000 });
      } else if (check === 'format') await perform(check, 'dotnet', ['format', 'Nova.slnx', '--verify-no-changes', '--no-restore']);
      else if (check === 'contrast') await perform(check, 'npm', ['run', 'check:contrast'], { cwd: join(root, 'Nova') });
      else if (SUITES[check]) {
        const args = ['test', '--project', SUITES[check], '--no-build', '--no-launch-profile', '--no-launch-profile-arguments',
          '--zero-tests-policy', 'strict', '--minimum-expected-tests', '1', '--timeout', '25m',
          '--report-trx', '--report-xunit-ctrf', '--report-xunit-ctrf-filename', 'results.json', '--results-directory', join(directory, check)];
        for (const filter of plan.filters) args.push('--filter-class', filter);
        await perform(check, 'dotnet', args, { suite: check, timeoutMs: 27 * 60000, env: { NOVA_TEST_ARTIFACTS: directory, NOVA_BROWSER_TRACE: '1' } });
      } else throw new Error('Unknown planned check: ' + check);
    }
    manifest.verdict = 'passed';
  } catch (error) {
    if (!manifest) throw error;
    manifest.verdict = abort.signal.aborted ? 'cancelled' : 'failed';
    manifest.error = error.message;
    console.error(error.message);
  } finally {
    if (manifest) {
      manifest.endedAt = new Date().toISOString();
      try {
        manifest.finalSource = sourceIdentity(root);
        if (manifest.source.fingerprint !== manifest.finalSource.fingerprint || manifest.source.head !== manifest.finalSource.head || manifest.source.branch !== manifest.finalSource.branch) {
          manifest.verdict = 'stale'; manifest.sourceChanged = true;
        }
      } catch (error) { manifest.verdict = 'failed'; manifest.finalSourceError = error.message; }
    }
    try { unlock?.(); } catch (error) {
      if (manifest) { manifest.verdict = 'failed'; manifest.cleanupError = error.message; }
      else console.error(error.message);
    }
    process.removeListener('SIGINT', stop); process.removeListener('SIGTERM', stop);
    if (manifest) writeJson(file, manifest);
  }
  console.log('\nVerification ' + manifest.verdict + ': ' + file);
  if (options.profile === 'quick') console.log('Focused execution only; this does not establish full PR readiness.');
  return manifest.verdict === 'passed' ? 0 : 1;
}

export async function main(args = process.argv.slice(2), cwd = process.cwd()) {
  if (Number(process.versions.node.split('.')[0]) < 24) throw new Error('Engineering commands require Node 24 or later.');
  const options = argumentsFor(args);
  if (options.action === 'help') {
    console.log('node eng/verify.mjs plan|run|status --profile quick|push|pre-pr|pre-merge|ci [--base <commit>]\nquick: --suite unit|integration|browser [--filter-class <pattern>]\nci: --install-browser\nnode eng/verify.mjs expect --session <id> --profile <profile> --base <commit>\nnode eng/verify.mjs defer --session <id> --reason <reason>');
    return 0;
  }
  const root = repositoryRoot(cwd);
  if (options.action === 'expect' || options.action === 'defer') {
    const checkpoints = await import('./lib/checkpoints.mjs');
    const result = options.action === 'expect' ? await checkpoints.expectCheckpoint(root, options) : await checkpoints.deferCheckpoint(root, options);
    console.log(JSON.stringify(result, null, 2)); return 0;
  }
  if (options.action === 'status') {
    const result = evidenceStatus(root, options.profile, { base: options.base ? resolveCommit(root, options.base) : undefined });
    console.log(options.json ? JSON.stringify(result, null, 2) : result.verdict + (result.path ? ': ' + result.path : ''));
    return result.current && result.verdict === 'passed' ? 0 : 1;
  }
  if (options.action === 'plan') { console.log(JSON.stringify(prepareRun(root, options), null, 2)); return 0; }
  return execute(root, options);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().then(code => { process.exitCode = code; }).catch(error => { console.error(error.message); process.exitCode = 1; });
}
