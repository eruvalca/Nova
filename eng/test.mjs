import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

const directory = join(dirname(fileURLToPath(import.meta.url)), 'tests');
const tests = readdirSync(directory).filter(name => name.endsWith('.test.mjs')).sort().map(name => join(directory, name));
if (!tests.length) throw new Error('No engineering tests found.');
const result = spawnSync(process.execPath, ['--test', ...tests], { stdio: 'inherit', windowsHide: true });
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
