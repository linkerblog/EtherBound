import type { components } from "../net/schema";
type Material = components["schemas"]["MaterialResponse"];

export async function loadMaterials(): Promise<Map<number, Material>> {
  const response = await fetch("/api/materials");
  if (!response.ok) throw new Error(`materials request failed: ${response.status}`);
  const values = (await response.json()) as Material[];
  return new Map(values.map((material) => [material.id, material]));
}
