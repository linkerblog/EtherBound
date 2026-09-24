import { expect, test, type Page } from "@playwright/test";

type Box = { x: number; y: number; width: number; height: number };

function expectBox(actual: Box | null, expected: Box): void {
  expect(actual).not.toBeNull();
  for (const key of ["x", "y", "width", "height"] as const) {
    expect(Math.abs(actual![key] - expected[key]), `${key} ${actual![key]} vs ${expected[key]}`).toBeLessThanOrEqual(1);
  }
}

async function position(page: Page): Promise<string> {
  // The HUD is hidden, not removed, outside GAME, so its readout keeps updating.
  return (await page.locator(".telemetry .readout").first().textContent()) ?? "";
}

async function holdKey(page: Page, key: string, ms: number): Promise<void> {
  await page.keyboard.down(key);
  await page.waitForTimeout(ms);
  await page.keyboard.up(key);
  // Let the prediction settle and the next telemetry sample land.
  await page.waitForTimeout(500);
}

test("framed viewport with GAME, DEBUG and LLM tabs", async ({ page, request }) => {
  // Running, not paused: a paused clock already stops Niko, which would hide a leaking key.
  await request.post("/api/game/new", { data: { seed: 7 } });
  await page.goto("/");
  const canvas = page.locator("canvas");
  await expect(canvas).toBeVisible();
  await expect(page.locator(".telemetry")).toBeVisible({ timeout: 30_000 });

  const tablist = page.getByRole("tablist", { name: "Views" });
  const tabs = tablist.getByRole("tab");
  await expect(tabs).toHaveText(["GAME", "DEBUG", "LLM"]);
  await expect(tablist.getByRole("tab", { name: "GAME" })).toHaveAttribute("aria-selected", "true");

  const viewport = { x: 20, y: 48, width: 1240, height: 652 };
  expectBox(await page.locator(".viewport").boundingBox(), viewport);
  await expect.poll(async () => (await canvas.boundingBox())?.width).toBe(viewport.width);
  expectBox(await canvas.boundingBox(), viewport);

  // Control: on GAME the same key does move Niko, so the DEBUG check below means something.
  const before = await position(page);
  await holdKey(page, "w", 800);
  await expect.poll(() => position(page)).not.toBe(before);

  await page.keyboard.press("Alt+2");
  await expect(tablist.getByRole("tab", { name: "DEBUG" })).toHaveAttribute("aria-selected", "true");
  await expect(page.locator("#debug-seed")).toBeVisible({ timeout: 30_000 });
  await expect(canvas).toBeHidden();
  expectBox(await canvas.boundingBox(), viewport);

  const hidden = await position(page);
  await holdKey(page, "w", 800);
  expect(await position(page)).toBe(hidden);

  await page.keyboard.press("Escape");
  await expect(tablist.getByRole("tab", { name: "GAME" })).toHaveAttribute("aria-selected", "true");
  await expect(canvas).toBeVisible();

  await page.keyboard.press("Alt+3");
  await expect(tablist.getByRole("tab", { name: "LLM" })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByText("NO MODEL CONNECTED")).toBeVisible();
});
