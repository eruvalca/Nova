import { createHash } from 'node:crypto';
import { lstatSync, readFileSync, readlinkSync, realpathSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';

export function git(root, args) {
  const result = spawnSync('git', args, { cwd: root, encoding: 'utf8', windowsHide: true, maxBuffer: 32 * 1024 * 1024 });
  if (result.error || result.status !== 0) throw new Error(result.error?.message || result.stderr.trim() || 'git failed');
  return result.stdout;
}

export function repositoryRoot(cwd = process.cwd()) {
  return realpathSync(git(cwd, ['rev-parse', '--show-toplevel']).trim());
}

export function resolveCommit(root, value) {
  if (!value || typeof value !== 'string') throw new Error('A comparison base is required. Pass --base <commit>.');
  return git(root, ['rev-parse', '--verify', '--end-of-options', value + '^{commit}']).trim();
}

export function changedPaths(root, base) {
  const commit = resolveCommit(root, base);
  // Disabling rename pairing deliberately includes both old and new paths.
  const tracked = [
    ['diff', '--name-only', '--no-renames', '-z', commit, 'HEAD', '--'],
    ['diff', '--cached', '--name-only', '--no-renames', '-z', '--'],
    ['diff', '--name-only', '--no-renames', '-z', '--'],
  ].map(args => git(root, args)).join('');
  const untracked = git(root, ['ls-files', '--others', '--exclude-standard', '-z']);
  return [...new Set((tracked + untracked).split('\0').filter(Boolean))].sort();
}

export function sourceIdentity(root) {
  const paths = [...new Set(git(root, ['ls-files', '--cached', '--others', '--exclude-standard', '-z']).split('\0').filter(Boolean))].sort();
  const indexModes = new Map(git(root, ['ls-files', '--stage', '-z']).split('\0').filter(Boolean).map(row => {
    const split = row.indexOf('\t');
    return [row.slice(split + 1), row.slice(0, 6)];
  }));
  const hash = createHash('sha256');
  // The index also participates in change selection, even when the working
  // copy reverses a staged edit. Include its identity to keep that plan fresh.
  hash.update(git(root, ['ls-files', '--stage', '-z']));
  let files = 0;
  for (const name of paths) {
    const file = join(root, name);
    let stat;
    try { stat = lstatSync(file); } catch (error) { if (error.code === 'ENOENT') continue; throw error; }
    if (!stat.isFile() && !stat.isSymbolicLink()) throw new Error('Unsupported verification input: ' + name);
    const mode = stat.isSymbolicLink() ? '120000' : process.platform === 'win32'
      ? indexModes.get(name) || '100644' : (stat.mode & 0o111) ? '100755' : '100644';
    const bytes = stat.isSymbolicLink() ? Buffer.from(readlinkSync(file)) : readFileSync(file);
    hash.update(mode + '\0' + name + '\0' + bytes.length + '\0').update(bytes);
    files++;
  }
  return {
    fingerprint: hash.digest('hex'), files,
    head: resolveCommit(root, 'HEAD'),
    branch: git(root, ['branch', '--show-current']).trim(),
    dirty: Boolean(git(root, ['status', '--porcelain', '--untracked-files=normal']).trim()),
  };
}
