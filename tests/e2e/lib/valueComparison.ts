import type { ComparisonResult, DashboardSnapshot, ToleranceConfig } from "./types";

export function compareDashboardValues(
  grafana: DashboardSnapshot,
  coralogix: DashboardSnapshot,
  tolerance: ToleranceConfig
): ComparisonResult[] {
  const coralogixByName = new Map(
    coralogix.panels.map((panel) => [normalizePanelName(panel.panelName), panel])
  );

  const results: ComparisonResult[] = [];
  let comparableCount = 0;

  for (const grafanaPanel of grafana.panels) {
    const key = normalizePanelName(grafanaPanel.panelName);
    const coralogixPanel = coralogixByName.get(key);
    const coralogixValue = coralogixPanel?.numericValues[0];
    const grafanaValue = grafanaPanel.numericValues[0];

    if (typeof grafanaValue !== "number" || typeof coralogixValue !== "number") {
      continue;
    }
    comparableCount += 1;

    const absoluteDelta = Math.abs(grafanaValue - coralogixValue);
    const denominator = Math.max(Math.abs(grafanaValue), 1e-9);
    const relativeDelta = absoluteDelta / denominator;
    const withinTolerance = absoluteDelta <= tolerance.absolute || relativeDelta <= tolerance.relative;

    results.push({
      panelName: grafanaPanel.panelName,
      grafanaValue,
      coralogixValue,
      withinTolerance,
      absoluteDelta,
      relativeDelta
    });
  }

  if (comparableCount === 0) {
    results.push({
      panelName: "__no_comparable_panels__",
      grafanaValue: Number.NaN,
      coralogixValue: Number.NaN,
      withinTolerance: false,
      absoluteDelta: Number.NaN,
      relativeDelta: Number.NaN
    });
  }

  return results;
}

function normalizePanelName(name: string): string {
  return name.trim().toLowerCase();
}
