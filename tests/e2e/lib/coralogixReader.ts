import type { Page } from "@playwright/test";
import { parseNumericValues } from "./panelSnapshot";
import type { DashboardSnapshot } from "./types";

const CORALOGIX_DASHBOARD_URL = "https://watcher.coralogix.com/#/dashboards";
const NO_DATA_PATTERN = /there is no data matching your query|no data/i;
const ERROR_PATTERN = /query failed|widget failed|rendering error|runtime error|exception/i;

export async function readCoralogixDashboard(page: Page, cxDashboardId: string): Promise<DashboardSnapshot> {
  await page.goto(`${CORALOGIX_DASHBOARD_URL}/${encodeURIComponent(cxDashboardId)}`, { waitUntil: "domcontentloaded" });
  await page.waitForSelector("[data-test='custom-dashboards-container']", { timeout: 30_000 });
  await page.waitForTimeout(1_500);

  const titleText = ((await page.locator("[data-test='cd-title-text']").first().textContent()) ?? "").trim();
  const dashboardName = titleText.replace(/^Dashboards\s*-\s*/i, "").trim() || cxDashboardId;
  const widgets = page.locator("[data-test^='cd-widget-container-']");
  const count = await widgets.count();
  const panels: DashboardSnapshot["panels"] = [];

  for (let index = 0; index < count; index += 1) {
    const widget = widgets.nth(index);
    if (!(await widget.isVisible().catch(() => false)))
      continue;

    const panelId =
      (await widget.getAttribute("data-test")) ?? `coralogix-widget-${index + 1}`;
    const panelName =
      ((await widget.locator("[data-test='cd-widget-name']").first().textContent().catch(() => "")) ?? "").trim()
      || `Widget ${index + 1}`;
    const rawText = ((await widget.innerText().catch(() => "")) ?? "").trim();

    panels.push({
      panelId,
      panelName,
      rawText,
      numericValues: parseNumericValues(rawText),
      hasNoData: NO_DATA_PATTERN.test(rawText),
      hasVisibleError: ERROR_PATTERN.test(rawText)
    });
  }

  return {
    pageKind: "coralogix",
    dashboardName,
    url: page.url(),
    panels
  };
}
