import type { DashboardSnapshot } from "./types";

export function ensureDashboardHasData(snapshot: DashboardSnapshot): { ok: boolean; reason?: string } {
  if (snapshot.panels.length === 0) {
    return {
      ok: false,
      reason: `${snapshot.pageKind} dashboard "${snapshot.dashboardName}" has no visible panels.`
    };
  }

  const hasContent = snapshot.panels.some((panel) => panel.rawText.trim().length > 0);
  if (!hasContent) {
    return {
      ok: false,
      reason: `${snapshot.pageKind} dashboard "${snapshot.dashboardName}" has empty panel output.`
    };
  }

  const hasDataPanel = snapshot.panels.some((panel) => !panel.hasNoData && !panel.hasVisibleError && panel.rawText.length > 0);
  if (!hasDataPanel) {
    return {
      ok: false,
      reason: `${snapshot.pageKind} dashboard "${snapshot.dashboardName}" has no data-bearing panels.`
    };
  }

  return { ok: true };
}
