import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const spriteTable = readFileSync(new URL("../src/game/objectSprites.ts", import.meta.url), "utf8");
const sheetModule = readFileSync(new URL("../src/game/objectSheets.ts", import.meta.url), "utf8");

test("every object sprite has exactly one static Vite import", () => {
  const files = [...spriteTable.matchAll(/file:\s*["']([^"']+)["']/g)].map((match) => match[1]!);
  assert.ok(files.length > 0);
  for (const file of files) {
    const escaped = file.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    const imports = sheetModule.match(new RegExp(`from ["']\\.\\.\\/\\.\\.\\/\\.\\.\\/src\\/sprites\\/object\\/${escaped}["']`, "g")) ?? [];
    assert.equal(imports.length, 1, `${file} should be statically imported exactly once`);
  }
});
