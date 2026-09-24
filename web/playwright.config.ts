import { defineConfig } from "@playwright/test";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const here = fileURLToPath(new URL(".", import.meta.url));
// A throwaway database: the visual run must never open the savegame.
const database = join(tmpdir(), `etherbound-visual-${process.pid}.db`).replace(/\\/g, "/");

export default defineConfig({
  testDir: "./tests/visual",
  // Every spec resets the one shared server with its own seed, so two workers would clobber each other.
  workers: 1,
  timeout: 120_000,
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.002 } },
  use: {
    baseURL: "http://127.0.0.1:5173",
    channel: "msedge",
    headless: true,
    viewport: { width: 1280, height: 720 },
    deviceScaleFactor: 1,
    launchOptions: { args: ["--use-angle=swiftshader"] },
  },
  webServer: [
    {
      command: "uv run uvicorn etherbound.app:app --host 127.0.0.1 --port 8000",
      cwd: join(here, "..", "server"),
      url: "http://127.0.0.1:8000/api/health",
      reuseExistingServer: false,
      timeout: 120_000,
      env: { ETHERBOUND_DATABASE_URL: `sqlite:///${database}` },
    },
    {
      command: "node ./node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5173 --strictPort",
      cwd: here,
      url: "http://127.0.0.1:5173",
      reuseExistingServer: false,
      timeout: 60_000,
    },
  ],
});
