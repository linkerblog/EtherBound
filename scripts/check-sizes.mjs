#!/usr/bin/env node
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const SCAN_ROOTS = ["server/src", "web/src", "BitCanvas", "launcher/src"];
const SOURCE = /\.(?:py|ts|tsx|js|cs)$/;
const LIMIT = 600;
// Boot docs are read in full at every cold agent start, so they are budgeted in characters.
const DOC_BUDGETS = {
  "CLAUDE.md": 2000,
  "AGENTS.md": 5000,
  "CONTEXT.md": 24000,
  "docs/utils/VISION.md": 17000,
};
const PLAN_LINES = 220;
const oversized = [];
const overBudget = [];

function byName(left, right) {
  return left.name.localeCompare(right.name);
}

function lineCount(text) {
  return text.split(/\r?\n/).length - (text.endsWith("\n") ? 1 : 0);
}

function scan(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true }).sort(byName)) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      scan(path);
      continue;
    }
    if (!entry.isFile() || !SOURCE.test(entry.name)) continue;
    const name = relative(ROOT, path).replaceAll("\\", "/");
    if (name === "web/src/net/schema.d.ts") continue;
    const count = lineCount(readFileSync(path, "utf8"));
    if (count > LIMIT) oversized.push({ name, count });
  }
}

for (const root of SCAN_ROOTS) scan(join(ROOT, root));

for (const [name, budget] of Object.entries(DOC_BUDGETS)) {
  const count = [...readFileSync(join(ROOT, name), "utf8")].length;
  if (count > budget) overBudget.push(`${name}: ${count} chars (budget ${budget})`);
}
// Only plans in progress: docs/done/ is not maintained and PENDING.md is a list, not a plan.
for (const entry of readdirSync(join(ROOT, "docs"), { withFileTypes: true }).sort(byName)) {
  if (!entry.isFile() || !entry.name.endsWith(".md") || entry.name === "PENDING.md") continue;
  const count = lineCount(readFileSync(join(ROOT, "docs", entry.name), "utf8"));
  if (count > PLAN_LINES) overBudget.push(`docs/${entry.name}: ${count} lines (budget ${PLAN_LINES})`);
}

if (oversized.length === 0) {
  console.log(`check-sizes: no source files over ${LIMIT} lines`);
} else {
  console.warn(`check-sizes: source files over ${LIMIT} lines (warning only)`);
  for (const file of oversized) console.warn(`  ${file.name}: ${file.count}`);
}

if (overBudget.length === 0) {
  console.log("check-sizes: no docs over budget");
} else {
  console.warn("check-sizes: docs over budget (warning only)");
  for (const line of overBudget) console.warn(`  ${line}`);
}
