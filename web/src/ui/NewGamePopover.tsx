import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactElement } from "react";
import { fetchGameState, requestNewGame } from "../net/client";
import type { GeneratorInfo } from "../net/protocol";
import { randomSeed } from "./genForm";

export function NewGamePopover({
  seed: initialSeed,
  generators,
  onClose,
  onSuccess,
}: {
  seed: number;
  generators: GeneratorInfo[];
  onClose: () => void;
  onSuccess: (seed: number, generator: string) => void;
}): ReactElement {
  const [seedText, setSeedText] = useState(String(initialSeed));
  const [generator, setGenerator] = useState("");
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const pendingRef = useRef(false);
  const seedRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    seedRef.current?.focus();
    seedRef.current?.select();
    let active = true;
    void fetchGameState()
      .then((state) => { if (active) setGenerator(state.generator); })
      .catch(() => {});
    return () => { active = false; };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();
    if (pendingRef.current) return;
    if (!/^\d+$/.test(seedText)) {
      setError("SEED MUST BE A WHOLE NUMBER FROM 0 TO 2147483647");
      return;
    }
    const seed = Number(seedText);
    if (!Number.isSafeInteger(seed) || seed < 0 || seed > 2147483647) {
      setError("SEED MUST BE A WHOLE NUMBER FROM 0 TO 2147483647");
      return;
    }

    pendingRef.current = true;
    setPending(true);
    setError("");
    try {
      await requestNewGame(seed, generator || undefined);
      onSuccess(seed, generator || "test");
    } catch (cause) {
      setError(cause instanceof Error ? cause.message.toUpperCase() : "NEW GAME REQUEST FAILED");
    } finally {
      pendingRef.current = false;
      setPending(false);
    }
  }

  function randomizeSeed(): void {
    setSeedText(String(randomSeed(Number(seedText))));
    setError("");
  }

  function handleKeyDown(event: ReactKeyboardEvent<HTMLFormElement>): void {
    if (event.key !== "Escape") return;
    event.preventDefault();
    event.stopPropagation();
    if (!pendingRef.current) onClose();
  }

  return <form id="new-game-dialog" role="dialog" aria-label="New game" aria-modal="false" className="hud-panel confirm" noValidate onSubmit={(event) => void submit(event)} onClick={(event) => event.stopPropagation()} onKeyDown={handleKeyDown}>
    <h3>NEW GAME</h3>
    <p>Wipes the city, Niko and the event log. This cannot be undone.</p>
    <div className="seed">
      <label className="label" htmlFor="new-game-map">MAP</label>
      <select id="new-game-map" value={generator} onChange={(event) => setGenerator(event.target.value)}>
        {generators.length === 0 && <option value="">test</option>}
        {generators.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
      </select>
    </div>
    <div className="seed">
      <label className="label" htmlFor="new-game-seed">SEED</label>
      <input ref={seedRef} id="new-game-seed" type="number" min="0" max="2147483647" step="1" value={seedText} aria-invalid={Boolean(error)} aria-describedby={error ? "new-game-error" : undefined} onChange={(event) => { setSeedText(event.target.value); setError(""); }} />
      <button className="mini" type="button" disabled={pending} onClick={randomizeSeed}>RANDOM</button>
    </div>
    {error && <p id="new-game-error" className="error" role="alert">{error}</p>}
    <div className="actions">
      <button type="button" disabled={pending} onClick={onClose}>CANCEL</button>
      <button className="danger" type="submit" disabled={pending}>{pending ? "WORKING" : "REGENERATE"}</button>
    </div>
  </form>;
}
