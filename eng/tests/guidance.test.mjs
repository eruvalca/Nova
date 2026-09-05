import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath } from 'node:url';
import { instructionsFor, parseAgent, renderCopilotAgent, checkMetadataAndLinks } from '../lib/guidance.mjs';
import { expectedHookManifests, mergeOwnedHooks } from '../lib/guidance-hooks.mjs';
import { repairHookManifests, reset } from '../../.agents/skills/impeccable/scripts/hook-admin.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
test('four-agent adapter rejects unknown schema instead of dropping provider behavior', () => {
  const source = readFileSync(join(root, '.agents/skills/impeccable/agents/impeccable_asset_producer.toml'), 'utf8');
  const agent = parseAgent(source, 'impeccable_asset_producer');
  const rendered = renderCopilotAgent(agent);
  assert.match(rendered, /name: impeccable-asset-producer/);
  assert.match(rendered, /\.agents\/skills\/impeccable\/scripts/);
  assert.throws(() => parseAgent(source.replace('name =', 'tools = ["shell"]\nname ='), agent.name), /Unknown agent schema/);
});

test('scope rules retain domain/UI coverage without attaching EF/API rules to every UI unit test', () => {
  const matches = path => instructionsFor(root, path).map(file => file.split('/').at(-1).replace('.instructions.md', ''));
  assert.ok(matches('Nova.UI/Features/Campaigns/Pages/CampaignDetail.razor.cs').includes('season-lifecycle'));
  assert.ok(matches('Nova.UI/Features/Campaigns/Pages/CampaignDetail.razor.cs').includes('placement-decisions'));
  assert.ok(matches('Nova.UI/Features/Campaigns/Pages/CampaignDetail.razor').includes('season-lifecycle'));
  assert.ok(matches('Nova.UI/Features/Campaigns/Pages/CampaignDetail.razor').includes('placement-decisions'));
  assert.ok(matches('Nova.Browser.Tests/CampaignDirectoryBrowserTests.cs').includes('browser-testing'));
  assert.ok(matches('Nova/Features/Campaigns/CampaignEndpointRouteBuilderExtensions.cs').includes('api-endpoints'));
  assert.ok(!matches('Nova.Unit.Tests/Components/DrawerTests.cs').includes('ef-core-tenancy'));
  assert.ok(!matches('Nova/Features/Campaigns/CampaignService.cs').includes('ui-design'));
  assert.ok(!matches('Nova/Features/Campaigns/CampaignService.cs').includes('api-endpoints'));
  assert.ok(matches('Nova/Components/Layout/NavMenu.razor.js').includes('navigation-design'));
});

test('metadata/link validation checks real references and ignores fenced example artifacts', t => {
  const fixture = mkdtempSync(join(tmpdir(), 'nova-guidance-'));
  t.after(() => rmSync(fixture, { recursive: true, force: true }));
  mkdirSync(join(fixture, '.github/instructions'), { recursive: true });
  mkdirSync(join(fixture, '.agents/skills/sample'), { recursive: true });
  writeFileSync(join(fixture, 'AGENTS.md'), '```md\n[example](missing.yml)\n```\n');
  writeFileSync(join(fixture, '.agents/skills/sample/SKILL.md'), '---\nname: sample\ndescription: Sample skill.\n---\n');
  assert.deepEqual(checkMetadataAndLinks(fixture).errors, []);
  writeFileSync(join(fixture, 'AGENTS.md'), '[actual](missing.md)\n');
  assert.match(checkMetadataAndLinks(fixture).errors[0], /Broken local guidance link/);
});

test('hook generation preserves user commands, unknown event entries and top-level settings', () => {
  const expected = expectedHookManifests()['.codex/hooks.json'];
  const user = { matcher: 'Write', hooks: [{ type: 'command', command: 'user-check' }, { type: 'command', command: 'node ".agents/skills/impeccable/scripts/hook.mjs"' }] };
  const merged = mergeOwnedHooks({ custom: 'retained', hooks: { PostToolUse: [user], OtherEvent: [{ command: 'other-user-check' }] } }, expected);
  assert.equal(merged.custom, 'retained');
  assert.deepEqual(merged.hooks.PostToolUse[0], { matcher: 'Write', hooks: [{ type: 'command', command: 'user-check' }] });
  assert.equal(merged.hooks.OtherEvent[0].command, 'other-user-check');
  assert.deepEqual(mergeOwnedHooks(merged, expected), merged);
  assert.throws(() => mergeOwnedHooks({ hooks: { Stop: {} } }, expected), /Malformed/);
});

test('design repair and reset use canonical discovery and preserve verification/user hooks', t => {
  const fixture = mkdtempSync(join(tmpdir(), 'nova-hook-admin-'));
  t.after(() => rmSync(fixture, { recursive: true, force: true }));
  mkdirSync(join(fixture, '.agents/skills/impeccable'), { recursive: true });
  for (const [file, data] of Object.entries(expectedHookManifests())) {
    mkdirSync(dirname(join(fixture, file)), { recursive: true });
    if (file === '.codex/hooks.json') data.hooks.Stop.unshift({ hooks: [{ command: 'user-stop' }] });
    writeFileSync(join(fixture, file), JSON.stringify(data));
  }
  repairHookManifests(fixture);
  const codex = JSON.parse(readFileSync(join(fixture, '.codex/hooks.json')));
  assert.equal(codex.hooks.PostToolUse.length, 1);
  assert.equal(codex.hooks.Stop.length, 2);
  assert.match(readFileSync(join(fixture, '.github/hooks/impeccable.json'), 'utf8'), /eng\/hooks\.mjs/);
  reset(fixture);
  const after = JSON.parse(readFileSync(join(fixture, '.codex/hooks.json')));
  assert.equal(after.hooks.PostToolUse, undefined);
  assert.equal(after.hooks.Stop.length, 2);
  assert.ok(after.hooks.UserPromptSubmit);
  assert.ok(readFileSync(join(fixture, '.github/hooks/nova-verification.json'), 'utf8').includes('agentStop'));
});
