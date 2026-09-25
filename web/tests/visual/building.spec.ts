import { expect, test } from "@playwright/test";

test("the lab structure walls read as solid at x1 and x2", async ({ context, page, request }) => {
  await request.post("/api/game/new", {
    data: { seed: 7, generator: "lab", options: { spawn_bay: "structure" }, paused: true },
  });
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
  await page.waitForTimeout(5_000);
  await page.addStyleTag({
    content:
      "html, body { overflow: hidden; }" +
      " .viewport > *:not(.world), .view-tabs { opacity: 0 !important; }" +
      " .viewport::before { display: none !important; }",
  });

  await expect(canvas).toHaveScreenshot("building-x1.png");
  await page.keyboard.press("=");
  await expect(canvas).toHaveScreenshot("building-x2.png");
});
