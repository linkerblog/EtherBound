#!/usr/bin/env node
// Starts the server and the web client together during development.
// `npm run dev` is the single entry point Dev-001 asks for; `npm run stop` tears both down.

import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = dirname(fileURLToPath(import.meta.url));
const processes = [];

function start(name, command, args, cwd) {
  const child = spawn(command, args, { cwd, shell: true, stdio: "inherit" });
  child.on("exit", (code) => {
    console.log(`[${name}] exited with code ${code}`);
  });
  processes.push(child);
}

start("server", "uv", ["run", "uvicorn", "etherbound.app:app", "--reload"], join(root, "..", "server"));
start("web", "npm", ["run", "dev"], join(root, "..", "web"));

function shutdown() {
  for (const child of processes) {
    child.kill();
  }
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);
