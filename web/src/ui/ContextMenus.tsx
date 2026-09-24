import { useEffect, useState, type ReactElement, type RefObject } from "react";
import type { ContextTarget } from "../game/MapScene";
import type { MenuEntry } from "../net/protocol";
import { firstAvailable, radialSlots, stepFocus } from "./radialMenu";

export type MenuState = { target: ContextTarget; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;
export type RadialState = { center: { x: number; y: number }; targetLabel?: string; entries: MenuEntry[]; error?: string } | null;

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

export function RadialMenu({ state, onPick, onClose }: { state: Exclude<RadialState, null>; onPick: (entry: MenuEntry) => void; onClose: () => void }): ReactElement {
  const { entries, center } = state;
  const slots = radialSlots(entries);
  const [focus, setFocus] = useState(() => firstAvailable(entries));
  const [shake, setShake] = useState(false);

  useEffect(() => { setFocus(firstAvailable(entries)); }, [entries]);

  const focused = entries[focus];
  const extent = slots.length > 0 ? Math.max(...slots.map((slot) => slot.radius)) + 96 : 160;
  const detail = state.error
    ? state.error
    : !focused
      ? "NO ACTIONS"
      : `${focused.label}${focused.subject ? ` ${focused.subject}` : ""}${focused.available ? "" : ` · ${focused.reason ?? "unavailable"}`}`;

  function pick(entry: MenuEntry | undefined): void {
    if (!entry) return;
    if (!entry.available) {
      setShake(true);
      window.setTimeout(() => setShake(false), 260);
      return;
    }
    onPick(entry);
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (entries.length === 0) return;
      if (event.key === "ArrowRight" || event.key === "ArrowDown") {
        event.preventDefault();
        event.stopImmediatePropagation();
        setFocus((current) => stepFocus(current, 1, entries.length));
      } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
        event.preventDefault();
        event.stopImmediatePropagation();
        setFocus((current) => stepFocus(current, -1, entries.length));
      } else if (event.key >= "1" && event.key <= "9") {
        const index = Number(event.key) - 1;
        if (index < entries.length) {
          event.preventDefault();
          event.stopImmediatePropagation();
          setFocus(index);
        }
      } else if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        event.stopImmediatePropagation();
        pick(entries[focus]);
      }
    };
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [entries, focus, onPick]);

  return <div
    className="radial"
    role="menu"
    aria-label="Actions around Niko"
    style={{ left: center.x, top: center.y }}
    onClick={(event) => { event.stopPropagation(); onClose(); }}>
    <svg className="radial-lines" width={extent * 2} height={extent * 2} viewBox={`${-extent} ${-extent} ${extent * 2} ${extent * 2}`} aria-hidden="true">
      {slots.map((slot) => <line key={slot.index} x1={0} y1={0} x2={slot.x} y2={slot.y} />)}
    </svg>
    <div className={`radial-hub ${shake ? "shake" : ""}`}>
      <div className="radial-target">{state.targetLabel ?? "ACTIONS"}</div>
      <div className={`radial-detail ${focused?.available === false ? "off" : ""}`}>{detail}</div>
    </div>
    {slots.map((slot, index) => {
      const entry = slot.entry;
      const classes = [
        "radial-slot",
        index === focus ? "focus" : "",
        entry.available ? "" : "off",
        entry.tags.includes("ether") ? "ether" : "",
        entry.tags.includes("illegal") ? "illegal" : "",
        entry.tags.includes("violent") ? "violent" : "",
      ].filter(Boolean).join(" ");
      return <button
        key={`${entry.op}-${slot.index}`}
        type="button"
        role="menuitem"
        aria-disabled={!entry.available}
        className={classes}
        style={{ left: slot.x, top: slot.y }}
        title={`${entry.label}${entry.subject ? ` ${entry.subject}` : ""}${entry.available ? "" : ` · ${entry.reason ?? "unavailable"}`}`}
        onMouseEnter={() => setFocus(index)}
        onClick={(event) => { event.stopPropagation(); pick(entry); }}>
        <span className="radial-op">{entry.label}</span>
        {entry.subject && <span className="radial-subject">{entry.subject}</span>}
      </button>;
    })}
  </div>;
}
