import type { components } from "../net/schema";

type ObjectKind = components["schemas"]["ObjectKindResponse"];

export async function loadObjectKinds(): Promise<Map<string, ObjectKind>> {
  const response = await fetch("/api/objects");
  if (!response.ok) throw new Error(`objects request failed: ${response.status}`);
  const values = (await response.json()) as ObjectKind[];
  return new Map(values.map((kind) => [kind.key, kind]));
}
