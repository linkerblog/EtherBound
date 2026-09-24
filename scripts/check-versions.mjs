#!/usr/bin/env node
// Enforces docs/utils/COMMITS.md [Sec. 2] and docs/utils/VERSION.md.
// No dependencies: the root tooling is plain npm, and this runs from a git hook.
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";

const ROOT = execFileSync("git", ["rev-parse", "--show-toplevel"], { encoding: "utf8" }).trim();
const VERSION_PATH = "docs/utils/VERSION.md";
const SEMVER = /^v\d+\.\d+\.\d+$/;
const OVERALL = /^Overall project version: `(v\d+\.\d+\.\d+)`\s*$/m;

const args = process.argv.slice(2);
const stagedMode = args.includes("--staged");
const messageFlag = args.indexOf("--message");
const messageFile = messageFlag >= 0 ? args[messageFlag + 1] : null;

const failures = [];
const fail = (message) => failures.push(message);

function git(parts) {
  return execFileSync("git", parts, { cwd: ROOT, encoding: "utf8" });
}

function readVersion(source) {
  try {
    if (source === "index") return git(["show", `:${VERSION_PATH}`]);
    if (source === "head") return git(["show", `HEAD:${VERSION_PATH}`]);
    return readFileSync(join(ROOT, VERSION_PATH), "utf8");
  } catch {
    return "";
  }
}

function parseVersion(text) {
  const overall = text.match(OVERALL);
  const modules = new Map();
  for (const line of text.split(/\r?\n/)) {
    if (!line.startsWith("|")) continue;
    const cells = line.split("|").slice(1, -1).map((cell) => cell.trim());
    if (cells.length < 3) continue;
    const [name, pathsCell, versionCell] = cells;
    if (!name || name === "Module" || name.startsWith("---")) continue;
    const paths = pathsCell
      .split(",")
      .map((path) => path.replace(/`/g, "").trim())
      .filter(Boolean);
    modules.set(name, { paths, version: versionCell.replace(/`/g, "").trim() });
  }
  return { overall: overall ? overall[1] : null, modules };
}

function parseSemver(version) {
  const match = /^v(\d+)\.(\d+)\.(\d+)$/.exec(version ?? "");
  return match ? match.slice(1).map(Number) : null;
}

function isPatchBump(from, to) {
  return to[0] === from[0] && to[1] === from[1] && to[2] === from[2] + 1;
}

function isOneLevelBump(from, to) {
  return (
    (to[0] === from[0] + 1 && to[1] === 0 && to[2] === 0) ||
    (to[0] === from[0] && to[1] === from[1] + 1 && to[2] === 0) ||
    (to[0] === from[0] && to[1] === from[1] && to[2] === from[2] + 1)
  );
}

function checkAlways(parsed) {
  if (!parsed.overall) {
    fail(`VERSION.md has no "Overall project version: \`vX.Y.Z\`" line`);
  }
  for (const [name, module] of parsed.modules) {
    if (!SEMVER.test(module.version)) {
      fail(`[${name}] version "${module.version}" is not vX.Y.Z`);
    }
    if (module.paths.length === 0) {
      fail(`[${name}] has no paths`);
      continue;
    }
    for (const path of module.paths) {
      const absolute = join(ROOT, path);
      if (!existsSync(absolute)) {
        fail(`[${name}] path does not exist: ${path}`);
      } else if (path.endsWith("/") && !statSync(absolute).isDirectory()) {
        fail(`[${name}] is not a directory: ${path}`);
      }
    }
  }
}

function moduleFor(parsed, file) {
  let best = null;
  for (const [name, module] of parsed.modules) {
    for (const path of module.paths) {
      const matches = path.endsWith("/") ? file.startsWith(path) : file === path;
      if (matches && (best === null || path.length > best.length)) {
        best = { name, length: path.length };
      }
    }
  }
  return best ? best.name : null;
}

function checkStaged(staged, head) {
  const touched = new Set();
  for (const file of git(["diff", "--cached", "--name-only", "--diff-filter=ACMR"])
    .split(/\r?\n/)
    .filter(Boolean)) {
    const name = moduleFor(staged, file);
    if (name) touched.add(name);
  }

  for (const [name, module] of staged.modules) {
    const headVersion = head.modules.get(name)?.version ?? "v0.0.0";
    const headSemver = parseSemver(headVersion);
    const stagedSemver = parseSemver(module.version);
    if (!headSemver || !stagedSemver) {
      fail(`[${name}] unparseable version`);
      continue;
    }
    if (touched.has(name)) {
      if (!isPatchBump(headSemver, stagedSemver)) {
        fail(`[${name}] has staged files but ${headVersion} -> ${module.version} is not a +0.0.1 bump`);
      }
    } else if (module.version !== headVersion) {
      fail(`[${name}] bumped ${headVersion} -> ${module.version} without a staged path`);
    }
  }
}

function checkMessage(staged) {
  const text = readFileSync(resolve(ROOT, messageFile), "utf8");
  const first = text.split(/\r?\n/, 1)[0].trim();
  if (!SEMVER.test(first)) {
    fail(`commit message first line "${first}" is not vX.Y.Z`);
  }
  if (first !== staged.overall) {
    fail(`commit version ${first} differs from VERSION.md ${staged.overall}`);
  }
  const previousLine = git(["log", "--format=%s", "-n", "50"])
    .split(/\r?\n/)
    .map((line) => line.trim())
    .find((line) => /^v\d/.test(line));
  const previous = parseSemver(previousLine);
  const next = parseSemver(first);
  if (!previous) {
    fail("no previous v-commit to compare against");
  } else if (next && !isOneLevelBump(previous, next)) {
    fail(`${previousLine} -> ${first} is not exactly one level above the previous version`);
  }
}

const stagedParsed = parseVersion(readVersion("index"));
const headParsed = parseVersion(readVersion("head"));
const base = stagedMode || messageFile ? stagedParsed : parseVersion(readVersion("worktree"));

checkAlways(base);
if (stagedMode) checkStaged(stagedParsed, headParsed);
if (messageFile) checkMessage(stagedParsed);

if (failures.length > 0) {
  console.error("check-versions: failed");
  for (const message of failures) console.error(`  - ${message}`);
  process.exit(1);
}
console.log("check-versions: ok");
