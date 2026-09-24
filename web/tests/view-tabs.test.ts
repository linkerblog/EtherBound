import assert from "node:assert/strict";
import test from "node:test";
import { VIEW_TABS, stepTab, tabFromHotkey, tabLabel, type HotkeyEvent } from "../src/ui/viewTabs";

function press(code: string, modifiers: Partial<HotkeyEvent> = {}): HotkeyEvent {
  return { key: code.replace("Digit", ""), code, altKey: true, ctrlKey: false, metaKey: false, shiftKey: false, ...modifiers };
}

test("tabs run game, debug, llm", () => {
  assert.deepEqual([...VIEW_TABS], ["game", "debug", "llm"]);
  assert.deepEqual(VIEW_TABS.map(tabLabel), ["GAME", "DEBUG", "LLM"]);
});

test("Alt plus a digit selects the matching tab", () => {
  assert.equal(tabFromHotkey(press("Digit1")), "game");
  assert.equal(tabFromHotkey(press("Digit2")), "debug");
  assert.equal(tabFromHotkey(press("Digit3")), "llm");
});

test("the hotkey follows the physical key, not the typed character", () => {
  assert.equal(tabFromHotkey({ ...press("Digit2"), key: "é" }), "debug");
});

test("anything but a bare Alt plus 1..3 is not a tab hotkey", () => {
  assert.equal(tabFromHotkey(press("Digit1", { altKey: false })), null);
  assert.equal(tabFromHotkey(press("Digit1", { ctrlKey: true })), null);
  assert.equal(tabFromHotkey(press("Digit1", { metaKey: true })), null);
  assert.equal(tabFromHotkey(press("Digit1", { shiftKey: true })), null);
  assert.equal(tabFromHotkey(press("Digit4")), null);
  assert.equal(tabFromHotkey(press("Digit0")), null);
  assert.equal(tabFromHotkey(press("Numpad1")), null);
});

test("stepping wraps both ways", () => {
  assert.equal(stepTab("game", 1), "debug");
  assert.equal(stepTab("llm", 1), "game");
  assert.equal(stepTab("game", -1), "llm");
  assert.equal(stepTab("debug", -1), "game");
  assert.equal(stepTab("debug", 4), "llm");
});
