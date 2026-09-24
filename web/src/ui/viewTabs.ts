/** The views the framed area can show, in tab order. */
export const VIEW_TABS = ["game", "debug", "llm"] as const;

export type ViewTab = typeof VIEW_TABS[number];

export type HotkeyEvent = { key: string; code: string; altKey: boolean; ctrlKey: boolean; metaKey: boolean; shiftKey: boolean };

export function tabLabel(tab: ViewTab): string {
  return tab.toUpperCase();
}

/**
 * `Alt+1..3` selects a tab. It reads `code`, not `key`, so the shortcut sits on the same physical
 * keys on every layout (AZERTY puts symbols on the unshifted digit row).
 */
export function tabFromHotkey(event: HotkeyEvent): ViewTab | null {
  if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return null;
  const match = /^Digit([1-9])$/.exec(event.code);
  if (!match) return null;
  return VIEW_TABS[Number(match[1]) - 1] ?? null;
}

/** The tab `step` places away from `current`, wrapping at both ends. */
export function stepTab(current: ViewTab, step: number): ViewTab {
  const count = VIEW_TABS.length;
  const index = VIEW_TABS.indexOf(current);
  return VIEW_TABS[(((index + step) % count) + count) % count]!;
}
