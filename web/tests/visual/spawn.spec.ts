import { expect, test } from "@playwright/test";

test("seed 7 spawn is stable at x1, x2 and x4", async ({ context, page, request }) => {
  await request.post("/api/game/new", { data: { seed: 7 } });
  await context.addInitScript(() => localStorage.setItem("etherbound.zoom", "1"));

  let lastChunkAt = 0;
  page.on("websocket", (socket) => {
    socket.on("framereceived", (frame) => {
      try {
        const text = typeof frame.payload === "string" ? frame.payload : frame.payload.toString();
        if (JSON.parse(text).type === "chunk") lastChunkAt = Date.now();
      } catch {
        // Frames outside the JSON protocol are not chunk pushes.
      }
    });
  });

  await page.goto("/");
  const canvas = page.locator("canvas");
  await expect(canvas).toBeVisible();

  const deadline = Date.now() + 60_000;
  while (lastChunkAt === 0 || Date.now() - lastChunkAt <= 1_000) {
    expect(Date.now(), "chunks stopped arriving").toBeLessThan(deadline);
    await page.waitForTimeout(200);
  }

  await page.waitForTimeout(2_000);
  // The DOM HUD (its clock moves) sits over the canvas: keep its layout, drop its paint.
  await page.addStyleTag({
    content:
      "html, body { overflow: hidden; }" +
      " main > *:not(.world) { opacity: 0 !important; }" +
      " main.shell::after { display: none !important; }",
  });

  await expect(canvas).toHaveScreenshot("spawn-x1.png");

  await page.keyboard.press("=");
  await expect(canvas).toHaveScreenshot("spawn-x2.png");

  await page.keyboard.press("=");
  await page.keyboard.press("=");
  await expect(canvas).toHaveScreenshot("spawn-x4.png");
});
