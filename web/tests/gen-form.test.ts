import assert from "node:assert/strict";
import test from "node:test";
import { defaultValues, fieldError, flattenOptions, rows, toOptions } from "../src/ui/genForm";
import type { OptionField } from "../src/net/protocol";

const fields: OptionField[] = [
  {
    path: "feature",
    label: "Feature",
    kind: "choice",
    default: "none",
    min: null,
    max: null,
    step: null,
    choices: ["none", "relief"],
    group: null,
  },
  {
    path: "spawn_bay",
    label: "Spawn Bay",
    kind: "choice",
    default: "feature",
    min: null,
    max: null,
    step: null,
    choices: ["steps", "feature"],
    group: null,
  },
  {
    path: "relief.amplitude",
    label: "Amplitude",
    kind: "int",
    default: 12,
    min: 0,
    max: 24,
    step: null,
    choices: [],
    group: "relief",
  },
  {
    path: "relief.gain",
    label: "Gain",
    kind: "float",
    default: 0.5,
    min: 0.1,
    max: 0.9,
    step: 0.05,
    choices: [],
    group: "relief",
  },
];

test("fields become one row each with their defaults", () => {
  const values = defaultValues(fields);
  const rendered = rows(fields, values);
  assert.equal(rendered.length, fields.length);
  assert.equal(rendered[0]?.value, "none");
  assert.equal(rendered[2]?.value, 12);
  assert.equal(rendered[0]?.error, null);
});

test("grouped rows hide until the controlling choice selects them", () => {
  const values = defaultValues(fields);
  assert.equal(rows(fields, values).filter((row) => row.hidden).length, 2);
  const shown = rows(fields, { ...values, feature: "relief" });
  assert.equal(shown.filter((row) => row.hidden).length, 0);
});

test("flat values rebuild the nested options and back", () => {
  const values = { ...defaultValues(fields), feature: "relief" as const, "relief.amplitude": 20 };
  const options = toOptions(fields, values);
  assert.deepEqual(options, {
    feature: "relief",
    spawn_bay: "feature",
    relief: { amplitude: 20, gain: 0.5 },
  });
  assert.deepEqual(flattenOptions(options, fields), values);
});

test("out-of-range numbers are flagged", () => {
  assert.equal(fieldError(fields[2]!, 999), "MAX 24");
  assert.equal(fieldError(fields[2]!, -1), "MIN 0");
  assert.equal(fieldError(fields[2]!, 12.5), "WHOLE NUMBER ONLY");
  assert.equal(fieldError(fields[3]!, 1.2), "MAX 0.9");
  assert.equal(fieldError(fields[3]!, "abc"), "NOT A NUMBER");
  assert.equal(fieldError(fields[1]!, "feature"), null);
  const flagged = rows(fields, { ...defaultValues(fields), "relief.amplitude": 999 });
  assert.equal(flagged[2]?.error, "MAX 24");
});
