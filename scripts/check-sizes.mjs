#!/usr/bin/env node
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const SCAN_ROOTS = ["server/src", "web/src", "BitCanvas", "launcher/src"];
const SOURCE = /\.(?:py|ts|tsx|js|cs)$/;
const LIMIT = 600;
const oversized = [];

function scan(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true }).sort((left, right) =>
    left.name.localeCompare(right.name),
  )) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      scan(path);
      continue;
    }
    if (!entry.isFile() || !SOURCE.test(entry.name)) continue;
    const name = relative(ROOT, path).replaceAll("\\", "/");
    if (name === "web/src/net/schema.d.ts") continue;
    const text = readFileSync(path, "utf8");
    const lines = text.split(/\r?\n/);
    const count = lines.length - (text.endsWith("\n") ? 1 : 0);
    if (count > LIMIT) oversized.push({ name, count });
  }
}

for (const root of SCAN_ROOTS) scan(join(ROOT, root));

if (oversized.length === 0) {
  console.log(`check-sizes: no source files over ${LIMIT} lines`);
} else {
  console.warn(`check-sizes: source files over ${LIMIT} lines (warning only)`);
  for (const file of oversized) console.warn(`  ${file.name}: ${file.count}`);
}
