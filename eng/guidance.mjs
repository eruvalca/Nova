#!/usr/bin/env node
import { repositoryRoot } from './lib/git.mjs';
import { instructionsFor, syncGuidance } from './lib/guidance.mjs';

try {
  const args = process.argv.slice(2);
  const json = args.includes('--json');
  const [command = 'check', path] = args.filter(arg => arg !== '--json');
  const root = repositoryRoot();
  if (command === 'explain') {
    if (!path) throw new Error('Usage: node eng/guidance.mjs explain <repo-relative-path>');
    process.stdout.write(JSON.stringify({ path, instructions: ['AGENTS.md', ...instructionsFor(root, path)] }, null, 2) + '\n');
  } else {
    if (!['check', 'sync'].includes(command)) throw new Error('Use check, sync, or explain <path>.');
    const result = syncGuidance(root, { write: command === 'sync' });
    const summary = { ok: result.ok, drift: result.drift, errors: result.errors, filesChecked: result.metrics.length, words: result.metrics.reduce((total, item) => total + item.words, 0) };
    process.stdout.write(JSON.stringify(json ? result : summary, null, 2) + '\n');
    process.exitCode = result.ok ? 0 : 1;
  }
} catch (error) { process.stderr.write(error.message + '\n'); process.exitCode = 1; }
