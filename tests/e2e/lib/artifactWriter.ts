import fs from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import type { ComparisonResult, DashboardSnapshot } from "./types";

interface ArtifactPayload {
  dashboardName: string;
  grafanaDashboardId: string;
  cxDashboardId: string;
  grafana: DashboardSnapshot;
  coralogix: DashboardSnapshot;
  comparisons: ComparisonResult[];
  errors: {
    grafana: string[];
    coralogix: string[];
  };
}

export async function writeArtifacts(
  rootDir: string,
  artifactsDir: string,
  payload: ArtifactPayload,
  pages: { grafana: Page; coralogix: Page }
): Promise<void> {
  const baseDir = path.resolve(rootDir, artifactsDir, sanitize(payload.dashboardName));
  fs.mkdirSync(baseDir, { recursive: true });

  fs.writeFileSync(path.join(baseDir, "comparison.json"), JSON.stringify(payload, null, 2));

  await pages.grafana.screenshot({
    path: path.join(baseDir, "grafana.png"),
    fullPage: true
  });

  await pages.coralogix.screenshot({
    path: path.join(baseDir, "coralogix.png"),
    fullPage: true
  });
}

function sanitize(value: string): string {
  return value.replace(/[^a-zA-Z0-9-_]/g, "_");
}
