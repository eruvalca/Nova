// Fixture-driven tests for check-roadmap.mjs.
//
//   node --test .agents/skills/sync-epic-roadmap/scripts/check-roadmap.Tests.mjs
//
// Every advertised check has a positive and a negative case, plus the CLI exit-code contract.
// The fixture io keeps these deterministic and offline: no network, no repository state.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

import { createFixtureIo, main, runChecks } from "./check-roadmap.mjs";

const EPIC = 100;
const SCRIPT = fileURLToPath(new URL("./check-roadmap.mjs", import.meta.url));

const block = (inner) =>
  `<!-- native-child-roadmap:start -->\n${inner}\n<!-- native-child-roadmap:end -->`;

/** Build an io for one scenario. Keys are issue numbers. */
function io({
  children = {},
  bodies = {},
  refs = {},
  closingPrs = {},
  onMain = {},
  links = {},
  gitAvailable = true,
} = {}) {
  const fixture = {
    gitAvailable,
    children: Object.fromEntries(
      Object.entries(children).map(([n, kids]) => [n, kids.map((k) => (typeof k === "number" ? { number: k, state: "OPEN" } : k))])
    ),
    issues: Object.fromEntries(Object.entries(bodies).map(([n, b]) => [n, { body: b }])),
    refs,
    closingPrs,
    onMain,
    links,
  };
  return createFixtureIo(fixture, "fixture/repo");
}

const check = (result, n) => result.findings.filter((f) => f.check === n).map((f) => f.msg);
const run = (opts) => runChecks(io(opts), { epic: EPIC });
const issue = (state) => ({ kind: "issue", state });
const pr = (merged, mergedIntoDefault, baseRefName = "main") => ({
  kind: "pr",
  state: merged ? "CLOSED" : "OPEN",
  merged,
  mergedIntoDefault,
  baseRefName,
});
const closer = (merged, mergedIntoDefault, number = 9, baseRefName = "main") => ({
  number,
  merged,
  mergedIntoDefault,
  baseRefName,
});

const delivered = (extra = {}) => ({
  children: { [EPIC]: [1, 2], 1: [], 2: [] },
  bodies: { [EPIC]: block("- [x] #1 complete via #9\n- [ ] #2") },
  refs: { [EPIC]: issue("OPEN"), 1: issue("CLOSED"), 2: issue("OPEN"), 9: pr(true, true) },
  closingPrs: { 1: [closer(true, true)] },
  ...extra,
});

test("clean tree reports nothing", () => {
  const result = run(delivered());
  assert.deepEqual(result.findings, []);
  assert.equal(result.blocks, 1);
});

test("check 1: a child returned by GitHub is missing from the block", () => {
  const result = run({
    children: { [EPIC]: [1], 1: [2, 3], 2: [], 3: [] },
    bodies: { [EPIC]: block("- [ ] #1"), 1: block("- [ ] #2") },
    refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN"), 2: issue("OPEN"), 3: issue("OPEN") },
  });
  assert.deepEqual(check(result, 1), ["#1 block omits child issue(s) returned by GitHub: #3"]);
});

test("check 1: an issue with no children must not carry a block", () => {
  const result = run({ children: { [EPIC]: [] }, bodies: { [EPIC]: block("nothing here") }, refs: { [EPIC]: issue("OPEN") } });
  assert.deepEqual(check(result, 1), ["#100 carries a native-child-roadmap block but has no child issues"]);
});

test("check 1: an issue with children but no block is reported", () => {
  const result = run({ children: { [EPIC]: [1], 1: [] }, bodies: {}, refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN") } });
  assert.deepEqual(check(result, 1), ["#100 has 1 child issue(s) but no native-child-roadmap block"]);
});

test("check 2: a checked box on an open issue is reported", () => {
  const result = run(delivered({
    children: { [EPIC]: [1, 2], 1: [], 2: [] },
    refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN"), 2: issue("OPEN"), 9: pr(true, true) },
  }));
  assert.deepEqual(check(result, 2), ["#100 marks #1 complete, but #1 is OPEN"]);
});

test("check 2: a closed child left unchecked is reported", () => {
  const result = run(delivered({
    children: { [EPIC]: [1, 2], 1: [], 2: [] },
    bodies: { [EPIC]: block("- [x] #1 complete via #9\n- [ ] #2") },
    refs: { [EPIC]: issue("OPEN"), 1: issue("CLOSED"), 2: issue("CLOSED"), 9: pr(true, true) },
    closingPrs: { 1: [closer(true, true)], 2: [closer(true, true, 10)] },
  }));
  assert.deepEqual(check(result, 2), ["#100 leaves #2 unchecked, but #2 is CLOSED"]);
});

test("check 2: closed without a closing pull request is not delivery", () => {
  const result = run(delivered({ closingPrs: { 1: [] } }));
  assert.deepEqual(check(result, 2), ["#100 marks #1 complete, but no closing pull request is recorded for #1"]);
});

test("check 2: a closing PR merged to a non-default branch is not delivery", () => {
  const result = run(delivered({ closingPrs: { 1: [closer(true, false, 9, "feature/x")] } }));
  assert.deepEqual(check(result, 2), [
    "#100 marks #1 complete, but no closing PR reached the default branch: #9 merged to feature/x",
  ]);
});

test("check 2: an unmerged closing PR is not delivery", () => {
  const result = run(delivered({ closingPrs: { 1: [closer(false, false)] } }));
  assert.deepEqual(check(result, 2), [
    "#100 marks #1 complete, but no closing PR reached the default branch: #9 unmerged",
  ]);
});

test("check 2: a stated count that disagrees with GitHub is reported", () => {
  const result = run({
    children: { [EPIC]: [{ number: 1, state: "OPEN" }], 1: [{ number: 2, state: "OPEN" }, { number: 3, state: "OPEN" }], 2: [], 3: [] },
    bodies: { [EPIC]: block("- **#1 — Slice** (0 of 3): #2, #3") },
    refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN"), 2: issue("OPEN"), 3: issue("OPEN") },
  });
  assert.deepEqual(check(result, 2), ['#100 states "0 of 3" for #1, but GitHub reports 0 of 2 children closed']);
});

test("check 3: a checklist entry that is not a child is rejected, even outside the tree", () => {
  const result = run(delivered({ bodies: { [EPIC]: block("- [x] #1 complete via #9\n- [x] #777") } }));
  assert.deepEqual(check(result, 3), ["#100 lists #777 in a checklist but #777 is not a child of #100"]);
});

test("check 3: an issue parented twice is reported", () => {
  const result = run({
    children: { [EPIC]: [1, 2], 1: [3], 2: [3], 3: [] },
    bodies: {},
    refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN"), 2: issue("OPEN"), 3: issue("OPEN") },
  });
  assert.deepEqual(check(result, 3), ["#3 is a child of both #1 and #2"]);
});

test("check 4: a completion claim on an unmerged PR is reported", () => {
  const result = run(delivered({ refs: { [EPIC]: issue("OPEN"), 1: issue("CLOSED"), 2: issue("OPEN"), 9: pr(false, false) } }));
  assert.deepEqual(check(result, 4), ["#100 cites PR #9 as complete/merged, but it did not reach the default branch"]);
});

test("check 4: a completion claim on a PR merged to a non-default branch is reported", () => {
  const result = run(delivered({ refs: { [EPIC]: issue("OPEN"), 1: issue("CLOSED"), 2: issue("OPEN"), 9: pr(true, false, "release/1") } }));
  assert.deepEqual(check(result, 4), ["#100 cites PR #9 as complete/merged, but it did not reach the default branch"]);
});

test("check 5: a revision cited as delivered must be on the default branch", () => {
  const sha = "1111111";
  const result = run(delivered({ bodies: { [EPIC]: block(`- [x] #1 merged at \`${sha}\``) }, onMain: { [sha]: false } }));
  assert.deepEqual(check(result, 5), [`#100 cites \`${sha}\` as delivered/merged, but it is not an ancestor of the default branch`]);
});

test("check 5: an off-main revision cited as a tested head is a note, not a finding", () => {
  const sha = "2222222";
  const result = run(delivered({ bodies: { [EPIC]: block(`- [x] #1 validated at \`${sha}\``) }, onMain: { [sha]: false } }));
  assert.deepEqual(check(result, 5), []);
  assert.ok(result.notes.some((n) => n.includes(sha)), "expected an informational note");
});

test("check 5: without a git checkout the check is skipped and said so", () => {
  const result = run(delivered({ gitAvailable: false, bodies: { [EPIC]: block("- [x] #1 merged at `3333333`") } }));
  assert.deepEqual(check(result, 5), []);
  assert.ok(result.notes.some((n) => n.startsWith("check 5 skipped")));
});

test("check 6: an unresolved reference and an unresolved issue are reported", () => {
  const withRef = run(delivered({ bodies: { [EPIC]: block("- [x] #1 complete via #9\n- see #404") } }));
  assert.deepEqual(check(withRef, 6), ["#100 references #404, which does not exist in fixture/repo"]);

  const unresolvedScope = run({ children: { [EPIC]: [1], 1: [] }, bodies: {}, refs: { [EPIC]: issue("OPEN") } });
  assert.deepEqual(check(unresolvedScope, 6), ["#1 does not resolve to an issue or PR"]);
});

test("check 7: an unresolvable blob link is reported", () => {
  const sha = "4444444";
  const body = block(`- [x] #1 complete via #9 — [record](https://github.com/eruvalca/Nova/blob/${sha}/docs/missing.md)`);
  const result = run(delivered({ bodies: { [EPIC]: body } }));
  assert.deepEqual(check(result, 7), [`#100 links ${sha}/docs/missing.md, which does not resolve`]);
});

test("check 7: a resolvable blob link passes", () => {
  const sha = "5555555";
  const body = block(`- [x] #1 complete via #9 — [record](https://github.com/eruvalca/Nova/blob/${sha}/docs/present.md)`);
  const result = run(delivered({ bodies: { [EPIC]: body }, links: { [`${sha}|docs/present.md`]: true } }));
  assert.deepEqual(result.findings, []);
});

test("the epic's own summary omissions are informational, not findings", () => {
  const result = run({
    children: { [EPIC]: [1, 2], 1: [], 2: [] },
    bodies: { [EPIC]: block("- **#1 — Slice**: the dated summary names #1 only") },
    refs: { [EPIC]: issue("OPEN"), 1: issue("OPEN"), 2: issue("OPEN") },
  });
  assert.deepEqual(check(result, 1), []);
  assert.ok(result.notes.some((n) => n.includes("epic summary, informational")));
});

test("CLI: exit 0 when clean, 1 with findings, 2 when it cannot run", () => {
  const dir = mkdtempSync(join(tmpdir(), "roadmap-check-"));
  const write = (name, value) => {
    const p = join(dir, name);
    writeFileSync(p, JSON.stringify(value), "utf8");
    return p;
  };
  const cleanPath = write("clean.json", {
    children: { 100: [], 101: [] },
    issues: { 100: { body: "" } },
    refs: { 100: { kind: "issue", state: "OPEN" }, 101: { kind: "issue", state: "OPEN" } },
  });

  const clean = spawnSync(process.execPath, [SCRIPT, "--fixture", cleanPath, "fixture/repo", "100"], { encoding: "utf8" });
  assert.equal(clean.status, 0, clean.stdout + clean.stderr);
  assert.match(clean.stdout, /Clean\./);

  const findingsPath = write("findings.json", {
    children: { 100: [101], 101: [] },
    issues: { 100: { body: "" } },
    refs: { 100: { kind: "issue", state: "OPEN" }, 101: { kind: "issue", state: "OPEN" } },
  });
  const findings = spawnSync(process.execPath, [SCRIPT, "--fixture", findingsPath, "fixture/repo", "100"], { encoding: "utf8" });
  assert.equal(findings.status, 1, findings.stdout + findings.stderr);
  assert.match(findings.stdout, /has 1 child issue\(s\) but no native-child-roadmap block/);

  const brokenPath = join(dir, "broken.json");
  writeFileSync(brokenPath, "{ not json", "utf8");
  const broken = spawnSync(process.execPath, [SCRIPT, "--fixture", brokenPath, "fixture/repo", "100"], { encoding: "utf8" });
  assert.equal(broken.status, 2, broken.stdout + broken.stderr);
  assert.match(broken.stderr, /checker failed/);
});

test("main() returns the exit code for a fixture run", () => {
  const dir = mkdtempSync(join(tmpdir(), "roadmap-main-"));
  const p = join(dir, "clean.json");
  writeFileSync(p, JSON.stringify({ children: { 100: [] }, issues: { 100: { body: "" } }, refs: { 100: issue("OPEN") } }), "utf8");
  assert.equal(main(["fixture/repo", "100", "--fixture", p]), 0);
});
