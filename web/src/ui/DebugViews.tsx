import { useEffect, useRef, useState, type ReactElement } from "react";
import { fetchGameState, requestNewGame } from "../net/client";
import type { GameStateResponse, GeneratorInfo } from "../net/protocol";
import { defaultValues, flattenOptions, randomSeed, rows, toOptions, type GenRow, type GenValue, type GenValues } from "./genForm";

export function DebugView({ generators }: { generators: GeneratorInfo[] }): ReactElement {
  return <section id="view-panel-debug" className="view-panel" role="tabpanel" aria-labelledby="view-tab-debug" onClick={(event) => event.stopPropagation()}>
    <div className="tabs" role="tablist" aria-label="Debug sections">
      <button className="tab active" id="debug-tab" role="tab" aria-selected="true" aria-controls="debug-panel">MAP</button>
    </div>
    <div className="view-content" id="debug-panel" role="tabpanel" aria-labelledby="debug-tab">
      <div className="view-column"><MapTab generators={generators} /></div>
    </div>
  </section>;
}

/** Reserves the place for the model log; Phase 3 fills it with every logged model decision. */
export function LlmView(): ReactElement {
  return <section id="view-panel-llm" className="view-panel" role="tabpanel" aria-labelledby="view-tab-llm" onClick={(event) => event.stopPropagation()}>
    <div className="view-content">
      <div className="debug-placeholder">
        <span className="placeholder-mark" aria-hidden="true">[ -- ]</span>
        <h3>NO MODEL CONNECTED</h3>
        <p>Model calls and the events they log will appear here.</p>
      </div>
    </div>
  </section>;
}

function MapTab({ generators }: { generators: GeneratorInfo[] }): ReactElement {
  const [state, setState] = useState<GameStateResponse | null>(null);
  const [key, setKey] = useState("");
  const [values, setValues] = useState<GenValues>({});
  const [seedText, setSeedText] = useState("0");
  const [status, setStatus] = useState("");
  const [pending, setPending] = useState(false);
  const initialised = useRef(false);

  useEffect(() => {
    let active = true;
    void fetchGameState()
      .then((loaded) => { if (active) { setState(loaded); setKey(loaded.generator); setSeedText(String(loaded.seed)); } })
      .catch(() => { if (active) setStatus("STATE UNAVAILABLE"); });
    return () => { active = false; };
  }, []);

  const spec = generators.find((item) => item.key === key);

  useEffect(() => {
    if (!spec || !state || initialised.current) return;
    const options = state.generator === spec.key ? state.gen_options : {};
    setValues(flattenOptions(options, spec.fields));
    initialised.current = true;
  }, [spec, state]);

  function chooseGenerator(nextKey: string): void {
    setKey(nextKey);
    const next = generators.find((item) => item.key === nextKey);
    setValues(next ? defaultValues(next.fields) : {});
    setStatus("");
  }

  function change(path: string, value: GenValue): void {
    setValues((current) => ({ ...current, [path]: value }));
    setStatus("");
  }

  if (!spec) {
    return <div className="debug-placeholder">
      <span className="placeholder-mark" aria-hidden="true">[ -- ]</span>
      <h3>LOADING GENERATORS</h3>
      <p>{status || "Reading the map catalog from the server."}</p>
    </div>;
  }

  const rendered = rows(spec.fields, values);
  const seedValid = /^\d+$/.test(seedText) && Number(seedText) <= 2147483647;
  const hasError = rendered.some((row) => !row.hidden && row.error !== null);

  async function regenerate(): Promise<void> {
    if (!spec) return;
    if (!seedValid) { setStatus("SEED MUST BE A WHOLE NUMBER"); return; }
    if (hasError) { setStatus("FIX THE HIGHLIGHTED FIELDS"); return; }
    setPending(true);
    setStatus("");
    try {
      await requestNewGame(Number(seedText), spec.key, toOptions(spec.fields, values));
      setStatus("REGENERATED");
    } catch {
      setStatus("REGENERATE FAILED");
    } finally {
      setPending(false);
    }
  }

  return <div className="gen-panel">
    <div className="gen-meta">
      <div className="readout"><span className="label">MAP</span><span>{spec.name}</span></div>
      <div className="readout"><span className="label">VERSION</span><span>v{spec.version}</span></div>
      <div className="readout"><span className="label">CURRENT</span><span>{state ? `${state.generator} v${state.gen_version}` : "—"}</span></div>
    </div>
    <div className="seed">
      <label className="label" htmlFor="debug-map">MAP</label>
      <select id="debug-map" value={spec.key} onChange={(event) => chooseGenerator(event.target.value)}>
        {generators.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
      </select>
    </div>
    <div className="seed">
      <label className="label" htmlFor="debug-seed">SEED</label>
      <input id="debug-seed" type="number" min="0" max="2147483647" value={seedText} aria-invalid={!seedValid} onChange={(event) => { setSeedText(event.target.value); setStatus(""); }} />
      <button className="mini" type="button" onClick={() => setSeedText(String(randomSeed(Number(seedText))))}>RANDOM</button>
    </div>
    <div className="gen-form">
      {rendered.filter((row) => !row.hidden).map((row) => <GenRowView key={row.field.path} row={row} onChange={change} />)}
    </div>
    {spec.bays.length > 0 && <div className="bay-list">
      <div className="feed-label">▍BAYS</div>
      {spec.bays.map((bay) => <div className="row" key={bay.key}><span className="slot">{bay.key.toUpperCase()}</span><span className="item">{bay.x},{bay.y} · {bay.width}×{bay.height}</span></div>)}
    </div>}
    {status && <p className="error" role="status">{status}</p>}
    <div className="actions">
      <button type="button" onClick={() => { setValues(defaultValues(spec.fields)); setStatus(""); }}>RESET</button>
      <button className="danger" type="button" disabled={pending} onClick={() => void regenerate()}>{pending ? "WORKING" : "REGENERATE"}</button>
    </div>
  </div>;
}

function GenRowView({ row, onChange }: { row: GenRow; onChange: (path: string, value: GenValue) => void }): ReactElement {
  const { field, value, error } = row;
  const id = `gen-${field.path}`;
  return <div className="gen-row">
    <label className="label" htmlFor={id}>{field.label}</label>
    {field.kind === "choice"
      ? <select id={id} value={String(value)} onChange={(event) => onChange(field.path, event.target.value)}>
        {field.choices.map((choice) => <option key={choice} value={choice}>{choice}</option>)}
      </select>
      : field.kind === "bool"
        ? <input id={id} type="checkbox" checked={Boolean(value)} onChange={(event) => onChange(field.path, event.target.checked)} />
        : <input id={id} type="number" value={String(value)} min={field.min ?? undefined} max={field.max ?? undefined} step={field.step ?? undefined} aria-invalid={Boolean(error)} onChange={(event) => onChange(field.path, event.target.value === "" ? "" : Number(event.target.value))} />}
    {error && <small className="why">{error}</small>}
  </div>;
}
