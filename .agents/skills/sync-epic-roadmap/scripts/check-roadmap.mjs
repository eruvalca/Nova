#!/usr/bin/env node
// Read-only roadmap checker for the native-child-roadmap blocks in GitHub issue bodies.
//
// Verifies the mechanical subset of the roadmap audit (checks 1-7 in
// references/roadmap-checks.md). It never writes to GitHub or to the repository.
//
//   node check-roadmap.mjs [owner/repo] [epic-number]
//
// Exit codes: 0 = clean, 1 = findings, 2 = checker could not run.
//
// Membership contract: a "listed child" is a `#N` token inside a marker block that GitHub
// returns as a sub-issue of that block's owner, or a `#N` in a checkbox line. Blocks mix
// prose and lists, so tokens are parsed, not lines.

import { execFileSync } from "node:child_process";

const REPO = process.argv[2] ?? "eruvalca/Nova";
const EPIC = Number(process.argv[3] ?? 163);
const MARK_START = "<!-- native-child-roadmap:start -->";
const MARK_END = "<!-- native-child-roadmap:end -->";
const COMPLETION_WORDS = /complete[d]?\s+(?:via|through)|merged\s+(?:in|at)|completed\s+by|delivered\s+(?:in|by|as)/i;
const TESTED_WORDS = /tested|validated|head|revision|manifest/i;

const findings = [];
const notes = [];
const cache = new Map();

// Documented contract: 2 means the checker itself could not run.
process.on("uncaughtException", (err) => {
  console.error(`checker failed: ${err?.message ?? err}`);
  process.exit(2);
});

function gh(args) {
  return execFileSync("gh", args, { encoding: "utf8", maxBuffer: 256 * 1024 * 1024 });
}

function api(path) {
  if (cache.has(path)) return cache.get(path);
  // No cross-run HTTP cache: a checker that reports stale facts is worse than a slow one.
  const value = JSON.parse(gh(["api", path]));
  cache.set(path, value);
  return value;
}

function paginated(path) {
  const out = gh(["api", path, "--paginate", "--jq", ".[] | @json"]);
  return out
    .split("\n")
    .filter((l) => l.trim())
    .map((l) => JSON.parse(l));
}

function childrenOf(n) {
  return paginated(`repos/${REPO}/issues/${n}/sub_issues`).map((c) => ({
    number: c.number,
    state: c.state,
    title: c.title,
  }));
}

/** Resolve a bare number to {kind, state, merged} — issues and PRs share numbering. */
function resolve(n) {
  try {
    const i = api(`repos/${REPO}/issues/${n}`);
    const isPr = Boolean(i.pull_request);
    let merged = null;
    if (isPr) merged = Boolean(api(`repos/${REPO}/pulls/${n}`).merged_at);
    return { kind: isPr ? "pr" : "issue", state: String(i.state).toUpperCase(), merged, title: i.title };
  } catch {
    return null;
  }
}

function blockOf(body) {
  const start = body.indexOf(MARK_START);
  const end = body.indexOf(MARK_END);
  if (start === -1 || end === -1 || end < start) return null;
  return body.slice(start + MARK_START.length, end);
}

function shaOnMain(sha) {
  try {
    execFileSync("git", ["merge-base", "--is-ancestor", sha, "origin/main"], { stdio: "ignore" });
    return true;
  } catch {
    return false;
  }
}

// check 5 needs a local checkout; degrade honestly rather than reporting false findings.
const gitAvailable = (() => {
  try {
    execFileSync("git", ["rev-parse", "--is-inside-work-tree"], { stdio: "ignore" });
    return true;
  } catch {
    return false;
  }
})();

function linkResolves(sha, path) {
  try {
    gh(["api", `repos/${REPO}/contents/${path}?ref=${sha}`, "--jq", ".sha"]);
    return true;
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------- walk the tree
const queue = [EPIC];
const scope = [];
const parentOf = new Map();
while (queue.length) {
  const n = queue.shift();
  if (scope.includes(n)) {
    findings.push({ check: 3, msg: `#${n} is reachable from more than one parent — a sub-issue has exactly one parent` });
    continue;
  }
  scope.push(n);
  for (const child of childrenOf(n)) {
    if (parentOf.has(child.number) && parentOf.get(child.number) !== n) {
      findings.push({
        check: 3,
        msg: `#${child.number} is a child of both #${parentOf.get(child.number)} and #${n}`,
      });
    }
    parentOf.set(child.number, n);
    queue.push(child.number);
  }
}

const state = new Map();
for (const n of scope) {
  const r = resolve(n);
  if (!r) {
    findings.push({ check: 6, msg: `#${n} does not resolve to an issue or PR` });
    continue;
  }
  state.set(n, r);
}

notes.push(`scope: ${scope.length} issues reachable from #${EPIC}`);
if (!gitAvailable) {
  notes.push("check 5 skipped: run from the repository checkout to verify cited revisions");
}

// ---------------------------------------------------------------- per-block checks
let blockCount = 0;

for (const n of scope) {
  const body = api(`repos/${REPO}/issues/${n}`).body ?? "";
  const block = blockOf(body);
  const kids = childrenOf(n);

  if (!block) {
    // Structural invariant: a block exists iff the issue has native children.
    if (kids.length) {
      findings.push({ check: 1, msg: `#${n} has ${kids.length} child issue(s) but no native-child-roadmap block` });
    }
    continue;
  }
  blockCount++;

  const kidSet = new Set(kids.map((k) => k.number));
  const lines = block.split(/\r?\n/);
  const tokens = new Set([...block.matchAll(/#(\d+)\b/g)].map((m) => Number(m[1])));

  // check 1 (forward) — every real child is named. #163 is a dated summary by design.
  const missing = kids.filter((k) => !tokens.has(k.number));
  if (missing.length) {
    const msg = `#${n} block omits child issue(s) returned by GitHub: ${missing.map((m) => `#${m.number}`).join(", ")}`;
    if (n === EPIC) notes.push(`check 1 (epic summary, informational): ${msg}`);
    else findings.push({ check: 1, msg });
  }

  // checks 2 and 3 — checkbox lines are delivery claims.
  for (const line of lines) {
    const box = line.match(/\[([ xX])\]\s*(?:\*\*)?#(\d+)\b/);
    if (!box) continue;
    const num = Number(box[2]);
    const owner = parentOf.get(num);
    if (!kidSet.has(num) && owner !== undefined && owner !== n) {
      findings.push({ check: 3, msg: `#${n} lists #${num} in a checklist but #${num} is a child of #${owner}` });
    }
    const r = state.get(num) ?? resolve(num);
    if (!r) continue;
    const checked = box[1].toLowerCase() === "x";
    if (checked && r.state !== "CLOSED") {
      findings.push({ check: 2, msg: `#${n} marks #${num} complete, but #${num} is ${r.state}` });
    }
    if (!checked && r.state === "CLOSED") {
      findings.push({ check: 2, msg: `#${n} leaves #${num} unchecked, but #${num} is CLOSED` });
    }
  }

  // check 2 — stated "N of M" counts. The count may describe a named child's children
  // (the epic summary does this), so the first issue on the line owns the count.
  for (const line of lines) {
    const counts = [...line.matchAll(/\((\d+)\s+of\s+(\d+)\)/g)];
    if (!counts.length) continue;
    const first = line.match(/#(\d+)\b/);
    const owner = first ? Number(first[1]) : n;
    const ownerKids = owner === n ? kids : childrenOf(owner);
    if (!ownerKids.length) continue;
    const done = ownerKids.filter((k) => String(k.state).toUpperCase() === "CLOSED").length;
    for (const m of counts) {
      if (Number(m[1]) !== done || Number(m[2]) !== ownerKids.length) {
        findings.push({
          check: 2,
          msg: `#${n} states "${m[1]} of ${m[2]}" for #${owner}, but GitHub reports ${done} of ${ownerKids.length} children closed`,
        });
      }
    }
  }

  // checks 4, 5, 6, 7 — PR and issue references, completion claims, revisions and links.
  for (const line of lines) {
    for (const m of line.matchAll(/#(\d+)\b/g)) {
      const num = Number(m[1]);
      const r = resolve(num);
      if (!r) {
        findings.push({ check: 6, msg: `#${n} references #${num}, which does not exist in ${REPO}` });
        continue;
      }
      if (r.kind === "pr" && COMPLETION_WORDS.test(line) && r.merged === false) {
        findings.push({ check: 4, msg: `#${n} cites PR #${num} as complete/merged, but it is not merged` });
      }
    }
    for (const m of line.matchAll(/`([0-9a-f]{7,40})`/g)) {
      const sha = m[1];
      if (!gitAvailable) continue;
      if (shaOnMain(sha)) continue;
      if (COMPLETION_WORDS.test(line)) {
        findings.push({ check: 5, msg: `#${n} cites \`${sha}\` as delivered/merged, but it is not an ancestor of origin/main` });
      } else if (TESTED_WORDS.test(line)) {
        notes.push(`check 5 (informational): #${n} cites \`${sha}\` as a tested revision; off-main, expected for a squash-merged head`);
      }
    }
    for (const m of line.matchAll(/github\.com\/[\w.-]+\/[\w.-]+\/(?:blob|tree)\/([0-9a-f]{7,40})\/([^\s)#]+)/g)) {
      const [, sha, path] = m;
      if (!linkResolves(sha, path)) {
        findings.push({ check: 7, msg: `#${n} links ${sha}/${path}, which does not resolve` });
      }
    }
  }
}

notes.push(`roadmap blocks: ${blockCount}`);

// ---------------------------------------------------------------- report
const byCheck = new Map();
for (const f of findings) byCheck.set(f.check, [...(byCheck.get(f.check) ?? []), f.msg]);

console.log(`Roadmap check — ${REPO} epic #${EPIC}`);
for (const note of notes) console.log(`  note  ${note}`);
console.log("");
for (const check of [1, 2, 3, 4, 5, 6, 7]) {
  if (check === 5 && !gitAvailable) {
    console.log("check 5: skipped (not in a git checkout)");
    continue;
  }
  const items = byCheck.get(check) ?? [];
  console.log(`check ${check}: ${items.length ? `${items.length} finding(s)` : "clean"}`);
  for (const msg of items) console.log(`  - ${msg}`);
}

if (findings.length) {
  console.log(`\n${findings.length} finding(s).`);
  process.exit(1);
}
console.log("\nClean.");
