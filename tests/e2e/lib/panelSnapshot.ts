export function parseNumericValues(value: string): number[] {
  const normalized = value.replace(/,/g, "");
  const matches = normalized.match(/-?\d+(\.\d+)?/g) ?? [];
  const numbers = matches.map((raw) => Number(raw)).filter((parsed) => Number.isFinite(parsed));
  return [...new Set(numbers)];
}
