#!/usr/bin/env node
// Publish ReadyToRun assemblies and replace the managed files in a Godot Windows export.
import { spawnSync } from "node:child_process";
import { copyFileSync, existsSync, mkdtempSync, readdirSync, rmSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const EXPORT_DATA = "data_EtherBound.Game_windows_x86_64";

function exportDirectory(args) {
  if (args.length === 0) return ROOT;
  if (args.length !== 2 || args[0] !== "--export" || !args[1] || args[1].startsWith("--")) {
    throw new Error("usage: node scripts/publish-game.mjs [--export DIR]");
  }
  return resolve(ROOT, args[1]);
}

function assertGameIsClosed(executable) {
  const command = "$target = [IO.Path]::GetFullPath($env:ETHERBOUND_EXE); " +
    "$running = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and " +
    "[IO.Path]::GetFullPath($_.ExecutablePath).Equals($target, [StringComparison]::OrdinalIgnoreCase) }; " +
    "if ($running) { Write-Output 'EtherBound.exe is running'; exit 2 }";
  const result = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], {
    cwd: ROOT,
    encoding: "utf8",
    env: { ...process.env, ETHERBOUND_EXE: executable },
    windowsHide: true,
  });
  if (result.error) throw result.error;
  if (result.status === 2) throw new Error("close the exported EtherBound.exe before publishing");
  if (result.status !== 0) {
    throw new Error(`could not verify that EtherBound.exe is closed: ${result.stderr || result.stdout}`);
  }
}

function run(command, args) {
  const result = spawnSync(command, args, { cwd: ROOT, encoding: "utf8", windowsHide: true });
  if (result.error) throw result.error;
  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.status !== 0) throw new Error(`${command} exited with status ${result.status}`);
}

try {
  if (process.platform !== "win32") throw new Error("the Windows export can only be updated on Windows");

  const exportDir = exportDirectory(process.argv.slice(2));
  const executable = join(exportDir, "EtherBound.exe");
  const runtimeDir = join(exportDir, EXPORT_DATA);
  if (!existsSync(executable) || !existsSync(runtimeDir) ||
      !existsSync(join(exportDir, "assets", "sprites")) || !existsSync(join(exportDir, "assets", "furniture"))) {
    throw new Error(`expected EtherBound.exe, ${EXPORT_DATA}, assets/sprites, and assets/furniture under ${exportDir}`);
  }
  assertGameIsClosed(executable);

  const temporaryDir = mkdtempSync(join(tmpdir(), "etherbound-publish-"));
  try {
    const publishedDir = join(temporaryDir, "published");
    run("dotnet", [
      "publish",
      "game/EtherBound.Game.csproj",
      "-c",
      "ExportRelease",
      "-r",
      "win-x64",
      "--self-contained",
      "true",
      "-p:PublishReadyToRun=true",
      "-o",
      publishedDir,
    ]);

    const simAssembly = join(publishedDir, "EtherBound.Sim.dll");
    if (!existsSync(simAssembly) || statSync(simAssembly).size <= 800 * 1024) {
      throw new Error("ReadyToRun publish did not produce an EtherBound.Sim.dll larger than 800 KB");
    }

    const files = readdirSync(publishedDir, { withFileTypes: true })
      .filter((entry) => entry.isFile() && /\.(dll|deps\.json|runtimeconfig\.json)$/i.test(entry.name))
      .map((entry) => entry.name);
    for (const required of ["EtherBound.Game.dll", "EtherBound.Host.dll", "EtherBound.Sim.dll", "EtherBound.Game.deps.json"]) {
      if (!files.includes(required)) throw new Error(`publish output is missing ${required}`);
    }

    for (const name of files) copyFileSync(join(publishedDir, name), join(runtimeDir, name));
    const sizeKb = Math.round(statSync(join(runtimeDir, "EtherBound.Sim.dll")).size / 1024);
    console.log(`publish-game: copied ${files.length} managed runtime files to ${runtimeDir}`);
    console.log(`publish-game: EtherBound.Sim.dll is ${sizeKb} KB (ReadyToRun)`);
  } finally {
    rmSync(temporaryDir, { recursive: true, force: true });
  }
} catch (error) {
  console.error(`publish-game: ${error.message}`);
  process.exitCode = 1;
}
