#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawnSync } from 'node:child_process';
import { repositoryRoot, sourceIdentity } from '../../../../eng/lib/git.mjs';
import { createDesignRun, safeRunId } from './lib/design-run-paths.mjs';

const TOOLS = new Set(['build-phase.mjs', 'comp-spec.mjs', 'comp-diff.mjs', 'font-match.mjs', 'generate-image.mjs', 'embed-prompt.mjs', 'concept-seed.mjs', 'critique-storage.mjs', 'context.mjs']);
export function designTool(name) {
  if (!TOOLS.has(name)) throw new Error('Use one of the known Impeccable artifact tools: ' + [...TOOLS].join(', '));
  return fileURLToPath(new URL(name, import.meta.url));
}

function main() {
  const [command, idArg, tool, ...args] = process.argv.slice(2);
  const root = repositoryRoot();
  if (command === 'start') {
    process.stdout.write(JSON.stringify(createDesignRun(root, sourceIdentity(root)), null, 2) + '\n');
    return;
  }
  if (!['exec', 'finish'].includes(command)) throw new Error('Usage: design-run.mjs start | exec <id> <tool.mjs> [args...] | finish <id>');
  const id = safeRunId(idArg);
  const directory = path.join(root, 'artifacts', 'design', id);
  if (!fs.existsSync(path.join(directory, 'source-start.json'))) throw new Error('Unknown design run. Use design-run.mjs start first.');
  if (command === 'finish') {
    const file = path.join(directory, 'source-final.json');
    fs.writeFileSync(file, JSON.stringify({ id, capturedAt: new Date().toISOString(), source: sourceIdentity(root) }, null, 2) + '\n');
    process.stdout.write(`Source snapshot: ${file}\nRetain the approved comp and provenance, final captures, reviewer disposition, and source manifests in .impeccable/evidence/${id}/. Keep intermediate output in this run directory.\n`);
    return;
  }
  const result = spawnSync(process.execPath, [designTool(tool), ...args], { cwd: root, env: { ...process.env, IMPECCABLE_RUN_ID: id }, stdio: 'inherit', windowsHide: true });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { main(); } catch (error) { process.stderr.write(error.message + '\n'); process.exitCode = 1; }
}
