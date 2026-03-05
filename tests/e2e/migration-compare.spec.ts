import path from "node:path";
import { expect, test } from "@playwright/test";
import { writeArtifacts } from "./lib/artifactWriter";
import { readCoralogixDashboard } from "./lib/coralogixReader";
import { ensureDashboardHasData } from "./lib/dataChecks";
import { findVisibleErrorMarkers } from "./lib/errorDetection";
import { getGrafanaFrameRoot, readGrafanaDashboard } from "./lib/grafanaReader";
import { loadSelectionContract, resolveCompletedDashboardsByName } from "./lib/selection";
import { compareDashboardValues } from "./lib/valueComparison";

const ROOT_DIR = process.cwd();
const contract = loadSelectionContract(ROOT_DIR);
const dashboards = resolveCompletedDashboardsByName(ROOT_DIR, contract);

test.describe("dashboard migration comparison", () => {
  test.skip(dashboards.length === 0, "No completed dashboards selected for migration comparison.");

  for (const dashboard of dashboards) {
    test(`validates "${dashboard.name}"`, async ({ page, context }) => {
      const coralogixPage = await context.newPage();
      const grafanaConsoleErrors = attachConsoleErrorCollector(page);
      const coralogixConsoleErrors = attachConsoleErrorCollector(coralogixPage);

      const grafanaSnapshot = await readGrafanaDashboard(page, dashboard.grafanaDashboardId);
      const coralogixSnapshot = await readCoralogixDashboard(coralogixPage, dashboard.cxDashboardId);

      const grafanaRoot = await getGrafanaFrameRoot(page);
      const grafanaErrors = await findVisibleErrorMarkers(grafanaRoot);
      const coralogixErrors = await findVisibleErrorMarkers(coralogixPage.locator("body"));

      const grafanaData = ensureDashboardHasData(grafanaSnapshot);
      const coralogixData = ensureDashboardHasData(coralogixSnapshot);

      const comparisons = compareDashboardValues(grafanaSnapshot, coralogixSnapshot, contract.tolerance);
      const mismatches = comparisons.filter((item) => !item.withinTolerance);
      const filteredGrafanaConsoleErrors = filterBenignConsoleErrors(grafanaConsoleErrors);
      const filteredCoralogixConsoleErrors = filterBenignConsoleErrors(coralogixConsoleErrors);

      await writeArtifacts(ROOT_DIR, contract.artifactsDir, {
        dashboardName: dashboard.name,
        grafanaDashboardId: dashboard.grafanaDashboardId,
        cxDashboardId: dashboard.cxDashboardId,
        grafana: grafanaSnapshot,
        coralogix: coralogixSnapshot,
        comparisons,
        errors: {
          grafana: [...grafanaErrors, ...filteredGrafanaConsoleErrors],
          coralogix: [...coralogixErrors, ...filteredCoralogixConsoleErrors]
        }
      }, {
        grafana: page,
        coralogix: coralogixPage
      });

      await coralogixPage.close();

      expect(grafanaErrors, `Visible errors on Grafana dashboard "${dashboard.name}".`).toEqual([]);
      expect(coralogixErrors, `Visible errors on Coralogix dashboard "${dashboard.name}".`).toEqual([]);
      expect(filteredGrafanaConsoleErrors, `Console errors on Grafana dashboard "${dashboard.name}".`).toEqual([]);
      expect(filteredCoralogixConsoleErrors, `Console errors on Coralogix dashboard "${dashboard.name}".`).toEqual([]);
      expect(grafanaData.ok, grafanaData.reason).toBe(true);
      expect(coralogixData.ok, coralogixData.reason).toBe(true);
      expect(
        mismatches,
        `Numeric value mismatches exceeded tolerance for "${dashboard.name}". Artifacts saved under ${path.join(contract.artifactsDir, dashboard.name)}.`
      ).toEqual([]);
    });
  }
});

function attachConsoleErrorCollector(page: import("@playwright/test").Page): string[] {
  const errors: string[] = [];
  page.on("console", (message) => {
    if (message.type() === "error") {
      errors.push(message.text());
    }
  });
  page.on("pageerror", (error) => {
    errors.push(error.message);
  });
  return errors;
}

function filterBenignConsoleErrors(errors: string[]): string[] {
  const ignoredPatterns = [
    /intercom/i,
    /recaptcha/i
  ];
  return errors.filter((error) => !ignoredPatterns.some((pattern) => pattern.test(error)));
}
