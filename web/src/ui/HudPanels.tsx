import { useRef, type KeyboardEvent as ReactKeyboardEvent, type ReactElement } from "react";
import type { WorldState } from "../net/protocol";
import { VIEW_TABS, stepTab, tabLabel, type ViewTab } from "./viewTabs";

export function CarryPanel({ actor }: { actor: WorldState["actors"][string] | undefined }): ReactElement {
  const carried = actor?.carried ?? [];
  const both = carried.find((item) => item.slot === "both");
  const left = carried.find((item) => item.slot === "left");
  const right = carried.find((item) => item.slot === "right");
  const back = carried.find((item) => item.slot === "back");
  const itemText = (item: typeof carried[number] | undefined): string => item
    ? `${item.name}${item.quantity > 1 ? ` ×${item.quantity}` : ""}`
    : "—";
  return <div className="hud-panel carry" aria-label="Carried objects">
    <h3>CARRY</h3>
    {both
      ? <CarryRow slot="HANDS" item={itemText(both)} />
      : <><CarryRow slot="L" item={itemText(left)} /><CarryRow slot="R" item={itemText(right)} /></>}
    <CarryRow slot="BACK" item={itemText(back)} />
    <div className={`load ${((actor?.load_kg ?? 0) > 10) ? "warn" : ""}`}>LOAD {(actor?.load_kg ?? 0).toFixed(1)} kg</div>
  </div>;
}

function CarryRow({ slot, item }: { slot: string; item: string }): ReactElement {
  return <div className="row"><span className="slot">{slot}</span><span className="item">{item}</span></div>;
}

export function ViewTabs({ view, onSelect }: { view: ViewTab; onSelect: (tab: ViewTab) => void }): ReactElement {
  const buttons = useRef(new Map<ViewTab, HTMLButtonElement>());

  function handleKeyDown(event: ReactKeyboardEvent<HTMLDivElement>): void {
    if (event.key !== "ArrowRight" && event.key !== "ArrowLeft") return;
    event.preventDefault();
    // Step from the focused tab: `Alt+digit` can change the view without moving focus.
    const focused = (event.target as HTMLElement).dataset.tab as ViewTab | undefined;
    const next = stepTab(focused ?? view, event.key === "ArrowRight" ? 1 : -1);
    onSelect(next);
    buttons.current.get(next)?.focus();
  }

  return <nav className="view-tabs" onClick={(event) => event.stopPropagation()}>
    <div role="tablist" aria-label="Views" onKeyDown={handleKeyDown}>
      {VIEW_TABS.map((tab, index) => <button
        key={tab}
        ref={(element) => { if (element) buttons.current.set(tab, element); else buttons.current.delete(tab); }}
        type="button"
        role="tab"
        id={`view-tab-${tab}`}
        data-tab={tab}
        className={`view-tab ${view === tab ? "active" : ""}`}
        aria-selected={view === tab}
        aria-controls={`view-panel-${tab}`}
        aria-keyshortcuts={`Alt+${index + 1}`}
        tabIndex={view === tab ? 0 : -1}
        onClick={() => onSelect(tab)}>{tabLabel(tab)}</button>)}
    </div>
    <span className="shell-label" aria-hidden="true">ETHERBOUND // LIVE SIMULATION</span>
  </nav>;
}
