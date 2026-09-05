import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';

export function safeRunId(id) {
  if (typeof id !== 'string' || !/^[A-Za-z0-9_-]{1,100}$/.test(id)) throw new Error('Invalid design run ID.');
  return id;
}

export function designArtifactPath(kind, ...segments) {
  if (!['build', 'review', 'mocks', 'critique'].includes(kind)) throw new Error('Unknown design artifact directory.');
  const id = process.env.IMPECCABLE_RUN_ID;
  return path.join(id ? path.join('artifacts', 'design', safeRunId(id)) : '.impeccable', kind, ...segments);
}

export function createDesignRun(root, source) {
  const id = new Date().toISOString().replace(/[:.]/g, '-') + '-' + randomUUID().slice(0, 8);
  const directory = path.join(root, 'artifacts', 'design', id);
  fs.mkdirSync(directory, { recursive: true });
  for (const kind of ['build', 'review', 'mocks', 'critique']) fs.mkdirSync(path.join(directory, kind));
  const manifest = { id, createdAt: new Date().toISOString(), source, directory: path.relative(root, directory).split(path.sep).join('/') };
  fs.writeFileSync(path.join(directory, 'source-start.json'), JSON.stringify(manifest, null, 2) + '\n', { flag: 'wx' });
  return manifest;
}

export function requireDesignRun(root = process.cwd()) {
  if (!process.env.IMPECCABLE_RUN_ID) throw new Error('Start a fresh design run with design-run.mjs start, then use design-run.mjs exec <id> <tool> ... .');
  const id = safeRunId(process.env.IMPECCABLE_RUN_ID || '');
  const manifest = path.join(root, 'artifacts', 'design', id, 'source-start.json');
  if (!fs.existsSync(manifest)) throw new Error('Start a fresh design run with design-run.mjs start, then use design-run.mjs exec <id> <tool> ... .');
  return id;
}

function canonical(file) {
  const tail = [];
  let parent = path.resolve(file);
  while (!fs.existsSync(parent)) {
    const next = path.dirname(parent);
    if (next === parent) return path.resolve(file);
    tail.unshift(path.basename(parent)); parent = next;
  }
  return path.join(fs.realpathSync(parent), ...tail);
}

export function assertWritableArtifact(file, root = process.cwd()) {
  const target = canonical(path.resolve(root, file));
  for (const kind of ['build', 'review', 'mocks', 'critique']) {
    const historical = canonical(path.join(root, '.impeccable', kind));
    const relative = path.relative(historical, target);
    if (relative === '' || (relative !== '..' && !relative.startsWith('..' + path.sep) && !path.isAbsolute(relative))) throw new Error(`Historical design evidence is read-only: ${file}. Start a fresh design run and write under artifacts/design/<id>/.`);
  }
  return file;
}
