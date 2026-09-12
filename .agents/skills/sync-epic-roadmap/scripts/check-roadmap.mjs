#!/usr/bin/env node
// Read-only roadmap checker for the native-child-roadmap blocks in GitHub issue bodies.
//
// Verifies the mechanical subset of the roadmap audit (checks 1-7 in
// references/roadmap-checks.md). It never writes to GitHub or to the repository.
//
//   node check-roadmap.mjs [owner/repo] [epic-number] [--fixture path.json]
//
// Exit codes: 0 = clean, 1 = findings, 2 = the checker could not run.
//
// Membership contract: a "listed child" is a `#N` token inside a marker block that GitHub
// returns as a sub-issue of that block's owner, or a `#N` in a checkbox line. Blocks mix
// prose and lists, so tokens are parsed, not lines.
//
// `runChecks(io)` is exported so the checks can be driven from fixtures; the CLI builds the
// real GitHub/git-backed io, or a fixture-backed io when `--fixture` is passed.

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";

export const MARK_START = "<!-- native-child-roadmap:start -->";
export const MARK_END = "<!-- native-child-roadmap:end -->";

const COMPLETION_WORDS = /complete[d]?\s+(?:via|through)|merged\s+(?:in|at)|completed\s+by|delivered\s+(?:in|by|as)/i;
const TESTED_WORDS = /tested|validated|head|revision|manifest/i;

export function blockOf(body) {
  const start = body.indexOf(MARK_START);
  const end = body.indexOf(MARK_END);
  if (start === -1 || end === -1 || end < start) return null;
  return body.slice(start + MARK_START.length, end);
}

const describeClosers = (closers) =>
  closers
    .map((c) => `#${c.number}${c.merged ? ` merged to ${c.baseRefName ?? "unknown"}` : " unmerged"}`)
    .join(", ");

/**
 * Run the mechanical checks against an io adapter:
 * childrenOf(n), issueBody(n), resolve(n), closingPrs(n), onMain(sha), linkResolves(sha, path),
 * gitAvailable.
 */
export function runChecks(io, { epic = 163 } = {}) {
  const findings = [];
  const notes = [];
  const push = (check, msg) => findings.push({ check, msg });

  // --- tree walk -----------------------------------------------------------
  const scope = [];
  const parentOf = new Map();
  const reportedMultiParent = new Set();
  const queue = [epic];
  while (queue.length) {
    const n = queue.shift();
    if (scope.includes(n)) {
      // Already reported when the second parent was discovered; do not double-report.
      if (!reportedMultiParent.has(n)) {
        push(3, `#${n} is reachable from more than one parent — a sub-issue has exactly one parent`);
      }
      continue;
    }
    scope.push(n);
    for (const child of io.childrenOf(n)) {
      const seen = parentOf.get(child.number);
      if (seen !== undefined && seen !== n) {
        push(3, `#${child.number} is a child of both #${seen} and #${n}`);
        reportedMultiParent.add(child.number);
      }
      parentOf.set(child.number, n);
      queue.push(child.number);
    }
  }

  const state = new Map();
  for (const n of scope) {
    const r = io.resolve(n);
    if (!r) push(6, `#${n} does not resolve to an issue or PR`);
    else state.set(n, r);
  }
  notes.push(`scope: ${scope.length} issues reachable from #${epic}`);

  // --- per-block checks ----------------------------------------------------
  let blockCount = 0;
  for (const n of scope) {
    const block = blockOf(io.issueBody(n) ?? "");
    const kids = io.childrenOf(n);
    const kidSet = new Set(kids.map((k) => k.number));

    if (!block) {
      if (kids.length) push(1, `#${n} has ${kids.length} child issue(s) but no native-child-roadmap block`);
      continue;
    }
    blockCount++;
    // The invariant runs both ways: a block belongs only to an issue with native children.
    if (!kids.length) {
      push(1, `#${n} carries a native-child-roadmap block but has no child issues`);
    }

    const lines = block.split(/\r?\n/);
    const tokens = new Set([...block.matchAll(/#(\d+)\b/g)].map((m) => Number(m[1])));

    const missing = kids.filter((k) => !tokens.has(k.number));
    if (missing.length) {
      const msg = `#${n} block omits child issue(s) returned by GitHub: ${missing.map((m) => `#${m.number}`).join(", ")}`;
      if (n === epic) notes.push(`check 1 (epic summary, informational): ${msg}`);
      else push(1, msg);
    }

    // Checkbox lines are delivery claims: the number must be a real child, the box must match
    // the issue state, and a checked box needs a closing PR that reached the default branch.
    for (const line of lines) {
      const box = line.match(/\[([ xX])\]\s*(?:\*\*)?#(\d+)\b/);
      if (!box) continue;
      const num = Number(box[2]);
      if (!kidSet.has(num)) {
        push(3, `#${n} lists #${num} in a checklist but #${num} is not a child of #${n}`);
      }
      const r = state.get(num) ?? io.resolve(num);
      if (!r) continue;
      const checked = box[1].toLowerCase() === "x";
      if (checked && r.state !== "CLOSED") {
        push(2, `#${n} marks #${num} complete, but #${num} is ${r.state}`);
        continue;
      }
      if (!checked && r.state === "CLOSED") {
        push(2, `#${n} leaves #${num} unchecked, but #${num} is CLOSED`);
      }
      if (checked) {
        const closers = io.closingPrs(num);
        if (!closers.length) {
          push(2, `#${n} marks #${num} complete, but no closing pull request is recorded for #${num}`);
        } else if (!closers.some((c) => c.merged && c.mergedIntoDefault)) {
          push(2, `#${n} marks #${num} complete, but no closing PR reached the default branch: ${describeClosers(closers)}`);
        }
      }
    }

    // Stated "(N of M)" counts belong to the first issue named on the line, or to the owner.
    for (const line of lines) {
      const counts = [...line.matchAll(/\((\d+)\s+of\s+(\d+)\)/g)];
      if (!counts.length) continue;
      const first = line.match(/#(\d+)\b/);
      const owner = first ? Number(first[1]) : n;
      const ownerKids = owner === n ? kids : io.childrenOf(owner);
      if (!ownerKids.length) continue;
      const done = ownerKids.filter((k) => String(k.state).toUpperCase() === "CLOSED").length;
      for (const m of counts) {
        if (Number(m[1]) !== done || Number(m[2]) !== ownerKids.length) {
          push(2, `#${n} states "${m[1]} of ${m[2]}" for #${owner}, but GitHub reports ${done} of ${ownerKids.length} children closed`);
        }
      }
    }

    // References, completion claims, cited revisions and links.
    for (const line of lines) {
      for (const m of line.matchAll(/#(\d+)\b/g)) {
        const num = Number(m[1]);
        const r = io.resolve(num);
        if (!r) {
          push(6, `#${n} references #${num}, which does not exist in ${io.repo}`);
          continue;
        }
        if (r.kind === "pr" && COMPLETION_WORDS.test(line) && !(r.merged && r.mergedIntoDefault)) {
          push(4, `#${n} cites PR #${num} as complete/merged, but it did not reach the default branch`);
        }
      }
      for (const m of line.matchAll(/`([0-9a-f]{7,40})`/g)) {
        if (!io.gitAvailable) continue;
        if (io.onMain(m[1])) continue;
        if (COMPLETION_WORDS.test(line)) {
          push(5, `#${n} cites \`${m[1]}\` as delivered/merged, but it is not an ancestor of the default branch`);
        } else if (TESTED_WORDS.test(line)) {
          notes.push(`check 5 (informational): #${n} cites \`${m[1]}\` as a tested revision; off-main, expected for a squash-merged head`);
        }
      }
      for (const m of line.matchAll(/github\.com\/[\w.-]+\/[\w.-]+\/(?:blob|tree)\/([0-9a-f]{7,40})\/([^\s)#]+)/g)) {
        if (!io.linkResolves(m[1], m[2])) push(7, `#${n} links ${m[1]}/${m[2]}, which does not resolve`);
      }
    }
  }

  notes.push(`roadmap blocks: ${blockCount}`);
  if (!io.gitAvailable) notes.push("check 5 skipped: run from a repository checkout to verify cited revisions");

  return { findings, notes, scope, blocks: blockCount, gitAvailable: io.gitAvailable };
}

export function formatReport(result, { repo, epic }) {
  const byCheck = new Map();
  for (const f of result.findings) byCheck.set(f.check, [...(byCheck.get(f.check) ?? []), f.msg]);

  const out = [`Roadmap check — ${repo} epic #${epic}`];
  for (const note of result.notes) out.push(`  note  ${note}`);
  out.push("");
  for (const check of [1, 2, 3, 4, 5, 6, 7]) {
    if (check === 5 && !result.gitAvailable) {
      out.push("check 5: skipped (not in a git checkout)");
      continue;
    }
    const items = byCheck.get(check) ?? [];
    out.push(`check ${check}: ${items.length ? `${items.length} finding(s)` : "clean"}`);
    for (const msg of items) out.push(`  - ${msg}`);
  }
  out.push(result.findings.length ? `\n${result.findings.length} finding(s).` : "\nClean.");
  return out.join("\n");
}

// ---------------------------------------------------------------- io adapters

/** io backed by the `gh` CLI and a local git checkout. */
export function createGhIo(repo) {
  const [owner, name] = repo.split("/");
  const cache = new Map();
  const gh = (args) => execFileSync("gh", args, { encoding: "utf8", maxBuffer: 256 * 1024 * 1024 });

  const api = (path) => {
    if (!cache.has(path)) cache.set(path, JSON.parse(gh(["api", path])));
    return cache.get(path);
  };

  const gitAvailable = (() => {
    try {
      execFileSync("git", ["rev-parse", "--is-inside-work-tree"], { stdio: "ignore" });
      return true;
    } catch {
      return false;
    }
  })();

  let defaultBranch;
  const branch = () => {
    if (defaultBranch === undefined) defaultBranch = api(`repos/${repo}`).default_branch;
    return defaultBranch;
  };

  const onMain = (sha) => {
    try {
      execFileSync("git", ["merge-base", "--is-ancestor", sha, `origin/${branch()}`], { stdio: "ignore" });
      return true;
    } catch {
      return false;
    }
  };

  const resolve = (n) => {
    try {
      const i = api(`repos/${repo}/issues/${n}`);
      const state = String(i.state).toUpperCase();
      if (!i.pull_request) return { kind: "issue", state, title: i.title };
      const pr = api(`repos/${repo}/pulls/${n}`);
      const base = pr.base?.ref;
      const merged = Boolean(pr.merged_at);
      return {
        kind: "pr",
        state,
        merged,
        mergedIntoDefault: merged && base === branch() && (!gitAvailable || onMain(pr.merge_commit_sha)),
        baseRefName: base,
        title: i.title,
      };
    } catch {
      return null;
    }
  };

  const closingPrs = (n) => {
    const q = `{ repository(owner: "${owner}", name: "${name}") { issue(number: ${n}) { closedByPullRequestsReferences(first: 10) { nodes { number merged baseRefName mergeCommit { oid } } } } } }`;
    const data = JSON.parse(gh(["api", "graphql", "-f", `query=${q}`]));
    return (data.data?.repository?.issue?.closedByPullRequestsReferences?.nodes ?? []).map((p) => ({
      number: p.number,
      merged: Boolean(p.merged),
      baseRefName: p.baseRefName,
      mergedIntoDefault:
        Boolean(p.merged) &&
        p.baseRefName === branch() &&
        (!gitAvailable || !p.mergeCommit?.oid || onMain(p.mergeCommit.oid)),
    }));
  };

  return {
    repo,
    gitAvailable,
    childrenOf: (n) =>
      gh(["api", `repos/${repo}/issues/${n}/sub_issues`, "--paginate", "--jq", ".[] | @json"])
        .split("\n")
        .filter((l) => l.trim())
        .map((l) => JSON.parse(l))
        .map((c) => ({ number: c.number, state: c.state, title: c.title })),
    issueBody: (n) => api(`repos/${repo}/issues/${n}`).body ?? "",
    resolve,
    closingPrs,
    onMain,
    linkResolves: (sha, path) => {
      try {
        gh(["api", `repos/${repo}/contents/${path}?ref=${sha}`, "--jq", ".sha"]);
        return true;
      } catch {
        return false;
      }
    },
  };
}

/** io backed by a recorded fixture, so the checks run offline and deterministically. */
export function createFixtureIo(fixture, repo = "fixture/repo") {
  return {
    repo,
    gitAvailable: fixture.gitAvailable !== false,
    childrenOf: (n) => fixture.children?.[n] ?? [],
    issueBody: (n) => fixture.issues?.[n]?.body ?? "",
    resolve: (n) => fixture.refs?.[n] ?? null,
    closingPrs: (n) => fixture.closingPrs?.[n] ?? [],
    onMain: (sha) => Boolean(fixture.onMain?.[sha]),
    linkResolves: (sha, path) => Boolean(fixture.links?.[`${sha}|${path}`]),
  };
}

// ---------------------------------------------------------------- CLI

export function main(argv) {
  const flag = argv.indexOf("--fixture");
  const fixturePath = flag === -1 ? null : argv[flag + 1];
  const positional = argv.filter((a, i) => !a.startsWith("--") && i !== flag + 1);
  const repo = positional[0] ?? "eruvalca/Nova";
  const epic = Number(positional[1] ?? 163);

  const io = fixturePath
    ? createFixtureIo(JSON.parse(readFileSync(fixturePath, "utf8")), repo)
    : createGhIo(repo);
  const result = runChecks(io, { epic });
  console.log(formatReport(result, { repo, epic }));
  return result.findings.length ? 1 : 0;
}

const isEntryPoint = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isEntryPoint) {
  process.on("uncaughtException", (err) => {
    console.error(`checker failed: ${err?.message ?? err}`);
    process.exit(2);
  });
  process.exit(main(process.argv.slice(2)));
}
