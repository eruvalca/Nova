// These are repository-owned adapters. Merge only our entries so local and
// team-owned hooks survive regeneration, repair, and design-hook reset.
export function hookCommand(provider, event, shell = 'bash') {
  const args = `--provider ${provider} --event ${event}`;
  if (shell === 'cmd') {
    const script = hookCommand(provider, event, 'powershell');
    return `powershell.exe -NoProfile -NonInteractive -EncodedCommand ${Buffer.from(script, 'utf16le').toString('base64')}`;
  }
  if (shell === 'powershell') return `$novaHookRoot = git rev-parse --show-toplevel; if ($LASTEXITCODE -eq 0) { & node (Join-Path $novaHookRoot 'eng/hooks.mjs') ${args} }`;
  return `node "$(git rev-parse --show-toplevel)/eng/hooks.mjs" ${args}`;
}

export function codexEntry(event) {
  return { hooks: [{ type: 'command', command: hookCommand('codex', event), commandWindows: hookCommand('codex', event, 'cmd'), timeout: event === 'stop' ? 30 : 10 }] };
}

export function copilotEntry(event) {
  return { type: 'command', bash: hookCommand('copilot', event), powershell: hookCommand('copilot', event, 'powershell'), timeoutSec: event === 'stop' ? 30 : 10 };
}

export function expectedHookManifests() {
  return {
    '.codex/hooks.json': { hooks: { SessionStart: [codexEntry('start')], UserPromptSubmit: [codexEntry('prompt')], PostToolUse: [codexEntry('edit')], Stop: [codexEntry('stop')] } },
    '.github/hooks/impeccable.json': { version: 1, hooks: { postToolUse: [copilotEntry('edit')] } },
    '.github/hooks/nova-verification.json': { version: 1, hooks: { sessionStart: [copilotEntry('start')], userPromptSubmitted: [copilotEntry('prompt')], agentStop: [copilotEntry('stop')] } },
  };
}

function ownedCommand(value) {
  return typeof value === 'string' && (value.includes('/eng/hooks.mjs') || /skills\/impeccable\/scripts\/hook(?:-probe|-before-edit|-after-edit|-stop)?\.mjs/.test(value));
}

export function withoutOwnedHook(entry) {
  if (!entry || typeof entry !== 'object') return entry;
  if (['command', 'commandWindows', 'bash', 'powershell', 'windows', 'linux', 'osx'].some(key => ownedCommand(entry[key]))) return null;
  if (!Array.isArray(entry.hooks)) return entry;
  const hooks = entry.hooks.map(withoutOwnedHook).filter(Boolean);
  return hooks.length ? { ...entry, hooks } : null;
}

export function mergeOwnedHooks(existing = {}, expected) {
  if (!existing || typeof existing !== 'object' || Array.isArray(existing) || (existing.hooks && (typeof existing.hooks !== 'object' || Array.isArray(existing.hooks)))) throw new Error('Malformed hook manifest; refusing to replace user-owned hooks.');
  const merged = { ...existing, ...expected, hooks: {} };
  for (const key of new Set([...Object.keys(existing.hooks || {}), ...Object.keys(expected.hooks)])) {
    if (existing.hooks?.[key] !== undefined && !Array.isArray(existing.hooks[key])) throw new Error(`Malformed hook event ${key}; refusing to replace it.`);
    const values = [...(existing.hooks?.[key] || []).map(withoutOwnedHook).filter(Boolean), ...(expected.hooks[key] || [])];
    if (values.length) merged.hooks[key] = values;
  }
  return merged;
}
