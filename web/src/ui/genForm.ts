import type { OptionField } from "../net/protocol";

export type GenValue = string | number | boolean;
export type GenValues = Record<string, GenValue>;

export function randomSeed(exclude?: number): number {
  const values = new Uint32Array(1);
  let seed: number;
  do {
    window.crypto.getRandomValues(values);
    seed = values[0]! & 0x7fffffff;
  } while (seed === exclude);
  return seed;
}

export type GenRow = {
  field: OptionField;
  value: GenValue;
  error: string | null;
  hidden: boolean;
};

/** A grouped field shows only while a top-level choice holds the group's name. */
export function isVisible(field: OptionField, fields: OptionField[], values: GenValues): boolean {
  if (!field.group) return true;
  return fields.some(
    (candidate) =>
      !candidate.group && candidate.kind === "choice" && values[candidate.path] === field.group,
  );
}

export function defaultValues(fields: OptionField[]): GenValues {
  const values: GenValues = {};
  for (const field of fields) values[field.path] = coerce(field, field.default);
  return values;
}

export function flattenOptions(
  options: Record<string, unknown>,
  fields: OptionField[],
): GenValues {
  const values = defaultValues(fields);
  for (const field of fields) {
    const raw = readPath(options, field.path);
    if (raw !== undefined && raw !== null) values[field.path] = coerce(field, raw);
  }
  return values;
}

export function rows(fields: OptionField[], values: GenValues): GenRow[] {
  return fields.map((field) => {
    const value = values[field.path] ?? coerce(field, field.default);
    return {
      field,
      value,
      error: fieldError(field, value),
      hidden: !isVisible(field, fields, values),
    };
  });
}

export function fieldError(field: OptionField, value: GenValue): string | null {
  if (field.kind === "bool" || field.kind === "choice") return null;
  if (value === "") return "REQUIRED";
  const number = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(number)) return "NOT A NUMBER";
  if (field.kind === "int" && !Number.isInteger(number)) return "WHOLE NUMBER ONLY";
  if (field.min !== null && number < field.min) return `MIN ${field.min}`;
  if (field.max !== null && number > field.max) return `MAX ${field.max}`;
  return null;
}

export function toOptions(fields: OptionField[], values: GenValues): Record<string, unknown> {
  const options: Record<string, unknown> = {};
  for (const field of fields) {
    const value = values[field.path];
    if (value === undefined) continue;
    writePath(options, field.path, coerce(field, value));
  }
  return options;
}

function coerce(field: OptionField, value: unknown): GenValue {
  if (field.kind === "bool") return Boolean(value);
  if (field.kind === "choice") return String(value);
  return Number(value);
}

function readPath(source: Record<string, unknown>, path: string): unknown {
  let current: unknown = source;
  for (const part of path.split(".")) {
    if (typeof current !== "object" || current === null) return undefined;
    current = (current as Record<string, unknown>)[part];
  }
  return current;
}

function writePath(target: Record<string, unknown>, path: string, value: GenValue): void {
  const parts = path.split(".");
  let current = target;
  for (const part of parts.slice(0, -1)) {
    const next = current[part];
    if (typeof next !== "object" || next === null) current[part] = {};
    current = current[part] as Record<string, unknown>;
  }
  current[parts[parts.length - 1]!] = value;
}
