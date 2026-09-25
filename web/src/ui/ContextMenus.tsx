import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactElement, type RefObject } from "react";
import type { ContextTarget } from "../game/MapScene";
import type { MenuEntry, MenuPlace } from "../net/protocol";
import { digitFocus, firstAvailable, groupByVerb, radialSlots, stepFocus, targetName, targetSlots, type VerbGroup } from "./radialMenu";

export type MenuState = { target: ContextTarget; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;
export type RadialState = {
  center: { x: number; y: number };
  /** Niko's tile when the menu opened; entry offsets are relative to it. */
  origin: { x: number; y: number; z: number };
  targetLabel?: string;
  entries: MenuEntry[];
  places: MenuPlace[];
  error?: string;
} | null;

/** A tile the radial points at, for the map to outline. */
export type FocusTile = { x: number; y: number; h: number };

export function ContextMenu({ state, viewportRef, onPick, onClose }: {
  state: Exclude<MenuState, null>;
  viewportRef: RefObject<HTMLDivElement | null>;
  onPick: (entry: MenuEntry) => void;
  onClose: () => void;
}): ReactElement {
  const entries = state.entries;
  const [focus, setFocus] = useState(0);

  useEffect(() => {
    const first = entries.findIndex((entry) => entry.available);
    setFocus(first === -1 ? 0 : first);
  }, [entries]);

  useEffect(() => {
    // Capture phase, so Enter picks here instead of focusing the input line.
    const onKeyDown = (event: KeyboardEvent) => {
      if (entries.length === 0) return;
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault();
        event.stopImmediatePropagation();
        const step = event.key === "ArrowDown" ? 1 : -1;
        setFocus((current) => (current + step + entries.length) % entries.length);
      } else if (event.key === "Enter") {
        event.preventDefault();
        event.stopImmediatePropagation();
        const entry = entries[focus];
        if (entry) onPick(entry);
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [entries, focus, onPick]);

  const area = viewportRef.current?.getBoundingClientRect() ?? { left: 0, top: 0, right: window.innerWidth, bottom: window.innerHeight };
  const left = Math.max(area.left, Math.min(state.target.screenX, area.right - 220));
  const top = Math.max(area.top, Math.min(state.target.screenY, area.bottom - 180));

  return <div className="menu" style={{ left, top }} onClick={(event) => event.stopPropagation()}>
    <div className="target">TILE {state.target.x},{state.target.y},{state.target.z}{state.targetLabel ? ` · ${state.targetLabel}` : ""}</div>
    {state.error ? <div className="menu-empty">{state.error}</div> : entries.length === 0 ? <div className="menu-empty">NO ACTIONS RETURNED</div> : entries.map((entry, index) => <button
      key={`${entry.op}-${index}`}
      className={["op", index === focus ? "focus" : "", entry.tags.includes("ether") ? "ether" : "", entry.tags.includes("illegal") ? "illegal" : "", entry.available ? "" : "off"].filter(Boolean).join(" ")}
      disabled={!entry.available}
      onMouseEnter={() => setFocus(index)}
      onClick={() => (entry.available ? onPick(entry) : onClose())}>
      <span>{entry.label}{entry.subject ? ` ${entry.subject}` : ""}</span>{entry.reason && <small className="why">{entry.reason}</small>}
    </button>)}
  </div>;
}

export function RadialMenu({ state, onPick, onClose, onFocusTile }: {
  state: Exclude<RadialState, null>;
  onPick: (entry: MenuEntry) => void;
  onClose: () => void;
  onFocusTile: (tile: FocusTile | null) => void;
}): ReactElement {
  const { entries, center, places, origin } = state;
  const groups = useMemo(() => groupByVerb(entries), [entries]);
  // Null shows the verbs; a group shows that verb's targets, placed by tile.
  const [level, setLevel] = useState<VerbGroup | null>(null);
  const [focus, setFocus] = useState(() => firstAvailable(groups));
  const [shake, setShake] = useState(false);
  const [hubHalf, setHubHalf] = useState(30);
  const hubRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setLevel(null);
    setFocus(firstAvailable(groups));
  }, [groups]);

  // Hub rows make the hub taller, and the up and down columns must clear it.
  useLayoutEffect(() => {
    const height = hubRef.current?.offsetHeight;
    if (height && Math.abs(height / 2 - hubHalf) > 0.5) setHubHalf(height / 2);
  });

  const verbSlots = radialSlots(groups);
  const tiles = level ? targetSlots(level.entries, hubHalf) : [];
  const length = level ? tiles.length : groups.length;
  const focusedGroup = level ? undefined : groups[focus];
  const focusedEntry = level ? tiles[focus]?.entry : focusedGroup?.entries.length === 1 ? focusedGroup.entries[0] : undefined;

  const place = focusedEntry ? places.find((item) => item.dx === focusedEntry.tile_dx && item.dy === focusedEntry.tile_dy) : undefined;
  const tileKey = place ? `${origin.x + place.dx},${origin.y + place.dy},${place.h}` : "";
  useEffect(() => {
    if (!tileKey) {
      onFocusTile(null);
      return;
    }
    const [x, y, h] = tileKey.split(",").map(Number);
    onFocusTile({ x: x!, y: y!, h: h! });
  }, [tileKey]);

  function describe(entry: MenuEntry): string {
    const name = targetName(entry, places);
    return `${entry.label}${name ? ` ${name}` : ""}${entry.available ? "" : ` · ${entry.reason ?? "unavailable"}`}`;
  }

  const detail = state.error
    ? state.error
    : focusedEntry
      ? describe(focusedEntry)
      : focusedGroup
        ? `${focusedGroup.label} · ${focusedGroup.entries.length} targets`
        : "NO ACTIONS";
  const detailOff = focusedEntry ? !focusedEntry.available : focusedGroup ? !focusedGroup.available : false;

  function pickEntry(entry: MenuEntry | undefined): void {
    if (!entry) return;
    if (!entry.available) {
      setShake(true);
      window.setTimeout(() => setShake(false), 260);
      return;
    }
    onPick(entry);
  }

  function pickGroup(group: VerbGroup | undefined): void {
    if (!group) return;
    // One entry needs no second step: the verb already names it.
    if (group.entries.length === 1) {
      pickEntry(group.entries[0]);
      return;
    }
    setLevel(group);
    setFocus(firstAvailable(targetSlots(group.entries, hubHalf).map((slot) => slot.entry)));
  }

  function back(): void {
    if (!level) return;
    setFocus(Math.max(0, groups.indexOf(level)));
    setLevel(null);
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (length === 0) return;
      const claim = () => {
        event.preventDefault();
        event.stopImmediatePropagation();
      };
      const typing = document.activeElement instanceof HTMLInputElement || document.activeElement instanceof HTMLTextAreaElement;
      if (event.key === "ArrowRight" || event.key === "ArrowDown") {
        claim();
        setFocus((current) => stepFocus(current, 1, length));
      } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
        claim();
        setFocus((current) => stepFocus(current, -1, length));
      } else if (event.key >= "1" && event.key <= "9") {
        const digit = Number(event.key);
        const next = level ? digitFocus(tiles, digit, focus) : digit - 1 < length ? digit - 1 : null;
        if (next !== null) {
          claim();
          setFocus(next);
        }
      } else if (event.key === "Enter" || event.key === " ") {
        claim();
        if (level) pickEntry(tiles[focus]?.entry);
        else pickGroup(groups[focus]);
      } else if (level && (event.key === "Escape" || (event.key === "Backspace" && !typing))) {
        // Level 2 steps back; in level 1 Escape reaches the app, which closes the radial.
        claim();
        back();
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [groups, level, tiles, focus, length, onPick]);

  const markers = (tags: string[], available: boolean) => [
    available ? "" : "off",
    tags.includes("ether") ? "ether" : "",
    tags.includes("illegal") ? "illegal" : "",
    tags.includes("violent") ? "violent" : "",
  ];
  const outer = level ? tiles.filter((slot) => slot.octant !== null) : verbSlots;
  // In level 2 a line reaches only the first slot of each column.
  const lineEnds = level ? tiles.filter((slot) => slot.octant !== null && slot.row === 0) : verbSlots;
  const extent = outer.length > 0 ? Math.max(...outer.map((slot) => Math.max(Math.abs(slot.x), Math.abs(slot.y)))) + 96 : 160;

  return <div
    className="radial"
    role="menu"
    aria-label={level ? `${level.label} targets around Niko` : "Actions around Niko"}
    style={{ left: center.x, top: center.y }}
    onClick={(event) => { event.stopPropagation(); onClose(); }}>
    <svg className="radial-lines" width={extent * 2} height={extent * 2} viewBox={`${-extent} ${-extent} ${extent * 2} ${extent * 2}`} aria-hidden="true">
      {lineEnds.map((slot, index) => <line key={index} x1={0} y1={0} x2={slot.x} y2={slot.y} />)}
    </svg>
    <div ref={hubRef} className={`radial-hub ${shake ? "shake" : ""}`}>
      {level
        ? <button type="button" className="radial-header" aria-label={`Back from ${level.label}`} onClick={(event) => { event.stopPropagation(); back(); }}>{level.label}</button>
        : <div className="radial-target">{state.targetLabel ?? "ACTIONS"}</div>}
      {tiles.map((slot, index) => slot.octant !== null ? null : <button
        key={`hub-${index}`}
        type="button"
        role="menuitem"
        aria-disabled={!slot.entry.available}
        className={["radial-row", index === focus ? "focus" : "", ...markers(slot.entry.tags, slot.entry.available)].filter(Boolean).join(" ")}
        title={describe(slot.entry)}
        onMouseEnter={() => setFocus(index)}
        onClick={(event) => { event.stopPropagation(); pickEntry(slot.entry); }}>
        {targetName(slot.entry, places) ?? slot.entry.label}
      </button>)}
      <div className={`radial-detail ${detailOff ? "off" : ""}`}>{detail}</div>
    </div>
    {level
      ? tiles.map((slot, index) => slot.octant === null ? null : <button
        key={`tile-${index}`}
        type="button"
        role="menuitem"
        aria-disabled={!slot.entry.available}
        className={["radial-slot", "radial-tile", index === focus ? "focus" : "", ...markers(slot.entry.tags, slot.entry.available)].filter(Boolean).join(" ")}
        style={{ left: slot.x, top: slot.y }}
        title={describe(slot.entry)}
        onMouseEnter={() => setFocus(index)}
        onClick={(event) => { event.stopPropagation(); pickEntry(slot.entry); }}>
        <span className="radial-op">{targetName(slot.entry, places) ?? slot.entry.label}</span>
      </button>)
      : verbSlots.map((slot, index) => {
        const group = slot.entry;
        const single = group.entries.length === 1 ? group.entries[0]! : null;
        const name = single ? targetName(single, places) : null;
        return <button
          key={group.op}
          type="button"
          role="menuitem"
          aria-disabled={!group.available}
          aria-haspopup={single ? undefined : "menu"}
          className={["radial-slot", index === focus ? "focus" : "", ...markers(group.tags, group.available)].filter(Boolean).join(" ")}
          style={{ left: slot.x, top: slot.y }}
          title={single ? describe(single) : `${group.label} · ${group.entries.length} targets`}
          onMouseEnter={() => setFocus(index)}
          onClick={(event) => { event.stopPropagation(); pickGroup(group); }}>
          <span className="radial-op">{group.label}</span>
          {name && <span className="radial-subject">{name}</span>}
        </button>;
      })}
  </div>;
}
