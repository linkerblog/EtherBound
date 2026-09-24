import { expect, test } from "@playwright/test";

test("seed 7 spawn is stable at x1, x2 and x4", async ({ context, page, request }) => {
  // Paused so the seeded Extras stand still on their start tiles and the shot is repeatable.
  await request.post("/api/game/new", { data: { seed: 7, paused: true } });
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

  // A paused clock sends no ticks, so give the first paint time to settle before the x1 shot.
  await page.waitForTimeout(5_000);
  // The DOM HUD (its clock moves), the active tab and the frame sit over the canvas: keep their
  // layout, drop their paint.
  await page.addStyleTag({
    content:
      "html, body { overflow: hidden; }" +
      " .viewport > *:not(.world), .view-tabs { opacity: 0 !important; }" +
      " .viewport::before { display: none !important; }",
  });

  await expect(canvas).toHaveScreenshot("spawn-x1.png");

  await page.keyboard.press("=");
  await expect(canvas).toHaveScreenshot("spawn-x2.png");

  await page.keyboard.press("=");
  await page.keyboard.press("=");
  await expect(canvas).toHaveScreenshot("spawn-x4.png");
});
