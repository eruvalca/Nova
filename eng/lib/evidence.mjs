import { mkdirSync, readFileSync, readdirSync, renameSync, writeFileSync, statSync } from 'node:fs';
import { dirname, join, relative, isAbsolute } from 'node:path';
import { randomUUID } from 'node:crypto';
import { sourceIdentity, resolveCommit } from './git.mjs';
import { POLICY_VERSION, verificationPlan, assessReport, SUITES } from './policy.mjs';

export function writeJson(file, value) {
  mkdirSync(dirname(file), { recursive: true });
  const temporary = file + '.' + randomUUID() + '.tmp';
  writeFileSync(temporary, JSON.stringify(value, null, 2) + '\n');
  renameSync(temporary, file);
}

export function readJson(file) { return JSON.parse(readFileSync(file, 'utf8')); }

function complete(record) {
  if (record.verdict !== 'passed' || !record.endedAt || record.error || record.cleanupError || record.sourceChanged) return false;
  try {
    const required = verificationPlan({ profile: record.profile, paths: record.changedPaths || [], suites: record.profile === 'quick' ? record.plan.suites : [], filters: record.plan.filters });
    if (JSON.stringify(required.checks) !== JSON.stringify(record.plan.checks)) return false;
    if (required.checks.some(name => record.steps.filter(step => step.name === name).length !== 1)) return false;
    return record.steps.every(step => {
      if (step.verdict !== 'passed' || step.exitCode !== 0 || step.cancelled || step.timedOut || step.cleanupError) return false;
      if (!SUITES[step.name]) return true;
      const reportPath = relative(dirname(record.path), step.reportPath);
      if (reportPath.startsWith('..') || isAbsolute(reportPath)) return false;
      return assessReport(readJson(step.reportPath), { suite: step.name, screenshots: record.optionalScreenshotsEnabled }).verdict === 'passed';
    });
  } catch { return false; }
}

export function evidenceStatus(root, profile, { after, base } = {}) {
  const directory = join(root, 'artifacts/verification');
  let entries;
  try { entries = readdirSync(directory, { withFileTypes: true }); }
  catch (error) { if (error.code === 'ENOENT') return { current: false, verdict: 'missing', profile }; throw error; }
  const records = entries.filter(entry => entry.isDirectory()).map(entry => {
    const path = join(directory, entry.name, 'manifest.json');
    let stat;
    try { stat = statSync(path); } catch (error) { if (error.code === 'ENOENT') return null; throw error; }
    try {
      const record = readJson(path);
      if (typeof record?.profile !== 'string' || typeof record?.startedAt !== 'string') throw new Error('Malformed manifest metadata.');
      return { ...record, path };
    } catch (error) { return { malformed: true, verdict: 'incomplete', path, startedAt: stat.mtime.toISOString(), error: error.message }; }
  }).filter(record => record && (record.malformed || record.profile === profile) && (!after || record.startedAt >= after))
    .sort((a, b) => b.startedAt.localeCompare(a.startedAt));
  const record = records[0];
  if (!record) return { current: false, verdict: 'missing', profile };
  if (record.malformed) return { current: false, verdict: 'incomplete', profile, path: record.path, error: record.error };
  const identity = sourceIdentity(root);
  let currentBase;
  try { currentBase = base || (record.baseReference ? resolveCommit(root, record.baseReference) : record.base); }
  catch { currentBase = null; }
  const current = record.policyVersion === POLICY_VERSION && record.source?.fingerprint === identity.fingerprint
    && record.finalSource?.fingerprint === identity.fingerprint && record.source?.head === identity.head
    && record.source?.branch === identity.branch && record.base === currentBase;
  const verdict = record.verdict === 'passed' && !complete(record) ? 'incomplete' : record.verdict;
  return { current, verdict: current ? verdict : 'stale', profile, runId: record.runId, path: record.path, record };
}
