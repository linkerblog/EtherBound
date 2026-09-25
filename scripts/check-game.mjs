#!/usr/bin/env node
// check:game: build the Godot C# project, then, when GODOT_BIN names a Godot .NET editor, run a
// headless import and fail on any engine or script error (Dev-025 [Sec. 3.2]).
import { spawnSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function run(command, args) {
  const result = spawnSync(command, args, { cwd: ROOT, encoding: "utf8" });
  return { status: result.status ?? 1, output: `${result.stdout ?? ""}${result.stderr ?? ""}` };
}

const build = run("dotnet", ["build", "game/EtherBound.Game.csproj", "-nologo", "-v", "q"]);
if (build.status !== 0) {
  console.error(build.output);
  console.error("check-game: dotnet build failed");
  process.exit(1);
}

const godot = process.env.GODOT_BIN;
if (!godot) {
  console.log("check-game: build ok; GODOT_BIN not set, headless import skipped");
  process.exit(0);
}

const imported = run(godot, ["--headless", "--path", "game", "--import"]);
const errors = imported.output.split(/\r?\n/).filter((line) => /\b(SCRIPT )?ERROR\b/.test(line));
if (imported.status !== 0 || errors.length > 0) {
  console.error(errors.join("\n") || imported.output);
  console.error("check-game: headless import failed");
  process.exit(1);
}
console.log("check-game: build and headless import ok");
