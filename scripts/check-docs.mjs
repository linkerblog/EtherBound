#!/usr/bin/env node
// Fails on a file-qualified section reference (`VISION.md` [Sec. 5]) whose file or numbered
// heading does not exist, so moving text between living docs cannot silently break citations.
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { basename, dirname, join, posix, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const LIVING_DIRS = ["", "docs", "docs/utils"];
const REFERENCE = /`([^`\s]+\.md)`\)?\s*\[Sec\.\s*([^\]]+)\]/g;
const SECTION = /^\d+(?:\.\d+)*/;

function markdownIn(directory) {
  return readdirSync(join(ROOT, directory), { withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(".md"))
    .map((entry) => posix.join(directory, entry.name))
    .sort();
}

const living = LIVING_DIRS.flatMap(markdownIn);
const livingSet = new Set(living);
const headings = new Map();

function sectionsOf(path) {
  if (!headings.has(path)) {
    const numbers = new Set();
    for (const line of readFileSync(join(ROOT, path), "utf8").split(/\r?\n/)) {
      const match = /^#{2,6}\s+(\d+(?:\.\d+)*)(?:\.|\s|$)/.exec(line);
      if (match) numbers.add(match[1]);
    }
    headings.set(path, numbers);
  }
  return headings.get(path);
}

// A reference names a path from the root, from docs/, or a bare basename.
function resolveTarget(target) {
  if (/(^|\/)done\//.test(target)) return { skip: true };
  for (const candidate of [target, posix.join("docs", target)]) {
    if (livingSet.has(candidate)) return { path: candidate };
  }
  const matches = living.filter((path) => basename(path) === basename(target));
  if (matches.length === 1) return { path: matches[0] };
  if (matches.length > 1) return { error: `ambiguous file ${target} (${matches.join(", ")})` };
  // Archived plans vanish when the phase closes; only a file that exists nowhere is an error.
  if (existsSync(join(ROOT, "docs/done", basename(target)))) return { skip: true };
  return { error: `missing file ${target}` };
}

// Code quoted with double backticks and fenced blocks are examples, not citations.
function citable(line) {
  return line.replace(/(`{2,})[\s\S]*?\1/g, "");
}

const errors = [];
for (const file of living) {
  let fenced = false;
  readFileSync(join(ROOT, file), "utf8")
    .split(/\r?\n/)
    .forEach((line, index) => {
      if (/^\s*```[^`]*$/.test(line)) fenced = !fenced;
      if (fenced) return;
      for (const [, target, list] of citable(line).matchAll(REFERENCE)) {
        const resolved = resolveTarget(target);
        if (resolved.skip) continue;
        const where = `${file}:${index + 1}`;
        if (resolved.error) {
          errors.push(`${where}: ${resolved.error}`);
          continue;
        }
        for (const item of list.split(",")) {
          const number = SECTION.exec(item.trim())?.[0];
          if (number && !sectionsOf(resolved.path).has(number)) {
            errors.push(`${where}: ${resolved.path} has no section ${number}`);
          }
        }
      }
    });
}

if (errors.length > 0) {
  console.error("check-docs: broken section references");
  for (const error of errors) console.error(`  ${error}`);
  process.exit(1);
}
console.log(`check-docs: ${living.length} living docs, every section reference resolves`);
