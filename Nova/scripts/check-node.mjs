#!/usr/bin/env node
//
// Zero-dependency Node.js version preflight for the Nova theme build
// (npm ci / npm run build:css). Reads the `engines.node` requirement in
// `Nova/package.json` (currently >= 20) and compares it against the
// running Node.js major version.
//
// Exit code 0 = the installed Node.js major satisfies engines.node; the
//               script prints the version found.
// Exit code 1 = Node.js is too old (or `engines.node` cannot be read); the
//               script prints a friendly message naming the requirement.
//
// The `CheckNodePrerequisite` target in `Nova/Nova.csproj` runs this script
// before restoring npm packages so a missing/too-old Node fails the build
// with a clear message instead of a cryptic MSB3073 / command-not-found error.

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

// `engines.node` in Nova/package.json, e.g. ">=20". The repo uses the simple
// ">=" form; the preflight fails loudly if the form ever changes so it cannot
// silently accept an unsupported Node.
const requirement = JSON.parse(
  readFileSync(join(__dirname, "..", "package.json"), "utf8"),
).engines?.node;
if (typeof requirement !== "string") {
  console.error("Nova/package.json does not declare engines.node.");
  process.exit(1);
}

const requiredMajorMatch = /^>=\s*(\d+)/.exec(requirement.trim());
if (requiredMajorMatch === null) {
  console.error(
    `Unsupported engines.node format in Nova/package.json: "${requirement}" (expected ">=").`,
  );
  process.exit(1);
}

const requiredMajor = Number.parseInt(requiredMajorMatch[1], 10);
const installedMajor = Number.parseInt(process.versions.node.split(".")[0], 10);

if (installedMajor >= requiredMajor) {
  console.log(`Node.js ${process.versions.node} found (major ${installedMajor} >= ${requiredMajor}).`);
  process.exit(0);
} else {
  console.error(
    `Node.js ${process.versions.node} is too old: engines.node "${requirement}" requires major ${requiredMajor}+. ` +
      `Install from https://nodejs.org and rebuild.`,
  );
  process.exit(1);
}
