import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, matchesGlob, resolve } from 'node:path';
import { expectedHookManifests, mergeOwnedHooks } from './guidance-hooks.mjs';

const SKILL = '.agents/skills/impeccable';
const LEGACY = '.github/skills/impeccable';
export const AGENTS = ['impeccable_asset_producer', 'impeccable_documenter', 'impeccable_finish_reviewer', 'impeccable_manual_edit_applier'];
const read = file => readFileSync(file, 'utf8').replaceAll('\r\n', '\n');

export function parseAgent(text, expectedName) {
  const match = text.replaceAll('\r\n', '\n').match(/^name = ("[^\n]+")\ndescription = ("[^\n]+")\nmodel_reasoning_effort = ("(?:medium|high)")\nnickname_candidates = (\[[^\n]+\])\ndeveloper_instructions = '''\n([\s\S]*?)\n'''\n?$/);
  if (!match) throw new Error(`Unknown agent schema/transform: ${expectedName}. Review the new field or TOML form before extending the four-agent adapter.`);
  const [name, description, effort, nicknames] = match.slice(1, 5).map(JSON.parse);
  if (name !== expectedName || !description || !Array.isArray(nicknames) || !nicknames.every(value => typeof value === 'string')) throw new Error(`Invalid agent metadata: ${expectedName}`);
  return { name, description, effort, nicknames, body: match[5] };
}

export function renderCopilotAgent(agent) {
  const name = agent.name.replaceAll('_', '-');
  // Agent Skills paths are shared. Only the native invocation token differs.
  const body = agent.body.replaceAll('$impeccable', '/impeccable');
  return `---\nname: ${name}\ndescription: ${JSON.stringify(agent.description)}\n---\n${body}\n`;
}

function files(directory, prefix = '') {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const relative = prefix ? prefix + '/' + entry.name : entry.name;
    return entry.isDirectory() ? files(join(directory, entry.name), relative) : [relative];
  });
}

export function instructionMetadata(text, file) {
  const frontmatter = text.match(/^---\n([\s\S]*?)\n---(?:\n|$)/)?.[1];
  if (!frontmatter) throw new Error(`Missing instruction frontmatter: ${file}`);
  const applyTo = frontmatter.match(/^applyTo: "([^"\n]+)"$/m)?.[1];
  const description = frontmatter.match(/^description: .+$/m)?.[0];
  if (!applyTo || !description) throw new Error(`Instruction needs applyTo and description: ${file}`);
  if (frontmatter.split('\n').some(line => !/^(applyTo|description): /.test(line))) throw new Error(`Unknown instruction metadata form: ${file}`);
  return { file, globs: applyTo.split(',').map(value => value.trim()) };
}

export function instructionsFor(root, path) {
  const directory = join(root, '.github/instructions');
  return readdirSync(directory).filter(name => name.endsWith('.instructions.md')).sort().map(name => instructionMetadata(read(join(directory, name)), '.github/instructions/' + name)).filter(item => item.globs.some(glob => matchesGlob(path.replaceAll('\\', '/'), glob))).map(item => item.file);
}

export function guidanceArtifacts(root) {
  const artifacts = new Map();
  const canonicalAgents = files(join(root, SKILL, 'agents')).filter(file => file.endsWith('.toml'));
  if (canonicalAgents.some(file => !AGENTS.includes(basename(file, '.toml'))) || canonicalAgents.length !== AGENTS.length) throw new Error('The reviewed generator supports exactly the four Impeccable agents.');
  for (const name of AGENTS) {
    const source = read(join(root, SKILL, 'agents', name + '.toml'));
    const agent = parseAgent(source, name);
    artifacts.set('.codex/agents/' + name + '.toml', source);
    artifacts.set('.github/agents/' + name.replaceAll('_', '-') + '.agent.md', renderCopilotAgent(agent));
  }
  // Temporary compatibility copy: remove only after all native discovery
  // smoke tests are recorded. Shared scripts and canonical paths stay intact.
  if (existsSync(join(root, LEGACY))) {
    const sharedFiles = files(join(root, SKILL)).filter(file => !file.startsWith('agents/'));
    if (files(join(root, LEGACY)).some(file => !sharedFiles.includes(file))) throw new Error('Unexpected legacy skill file; reconcile it with the canonical skill before syncing.');
    for (const file of sharedFiles) {
      if (!/\.(?:md|mjs|js|json|yaml)$/.test(file)) throw new Error(`Unknown legacy skill transform: ${file}`);
      const source = read(join(root, SKILL, file));
      artifacts.set(LEGACY + '/' + file, file.endsWith('.md') ? source.replaceAll('$impeccable', '/impeccable') : source);
    }
  }
  for (const [file, expected] of Object.entries(expectedHookManifests())) {
    const existing = existsSync(join(root, file)) ? JSON.parse(read(join(root, file))) : {};
    artifacts.set(file, JSON.stringify(mergeOwnedHooks(existing, expected), null, 2) + '\n');
  }
  return artifacts;
}

export function checkMetadataAndLinks(root) {
  const errors = [];
  const metrics = [];
  const instructionFiles = files(join(root, '.github/instructions')).filter(file => file.endsWith('.instructions.md')).map(file => '.github/instructions/' + file);
  const skillFiles = files(join(root, '.agents/skills')).filter(file => file.endsWith('.md')).map(file => '.agents/skills/' + file);
  for (const file of ['AGENTS.md', ...instructionFiles, ...skillFiles]) {
    const content = read(join(root, file));
    metrics.push({ file, words: content.trim().split(/\s+/).length });
    try {
      if (instructionFiles.includes(file)) instructionMetadata(content, file);
      if (file.endsWith('/SKILL.md')) {
        const metadata = content.match(/^---\n([\s\S]*?)\n---/)?.[1];
        const name = metadata?.match(/^name: ([a-z0-9-]+)$/m)?.[1];
        if (name !== basename(dirname(file)) || !metadata?.match(/^description: \S/m)) throw new Error(`Invalid skill name/description metadata: ${file}`);
      }
      // Validate literal local Markdown links. URLs, anchors and parameterized
      // examples are not filesystem paths; executable recipes have their own tests.
      const prose = content.replace(/^(```|~~~)[^\n]*\n[\s\S]*?^\1[^\n]*$/gm, '');
      for (const match of prose.matchAll(/(?<!!)\[[^\]\n]+\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) {
        const target = match[1].replace(/^<|>$/g, '').split('#')[0];
        if (!target || /^[a-z][a-z0-9+.-]*:/i.test(target) || /[<>${}]/.test(target) || target.startsWith('/')) continue;
        if (!existsSync(resolve(root, dirname(file), decodeURIComponent(target)))) throw new Error(`Broken local guidance link: ${file} -> ${target}`);
      }
    } catch (error) { errors.push(error.message); }
  }
  return { errors, metrics };
}

export function syncGuidance(root, { write = false } = {}) {
  const drift = [];
  for (const [file, expected] of guidanceArtifacts(root)) {
    if (existsSync(join(root, file)) && read(join(root, file)) === expected) continue;
    drift.push(file);
    if (write) { mkdirSync(dirname(join(root, file)), { recursive: true }); writeFileSync(join(root, file), expected); }
  }
  const { errors, metrics } = checkMetadataAndLinks(root);
  return { ok: errors.length === 0 && (write || drift.length === 0), drift, errors, metrics };
}
