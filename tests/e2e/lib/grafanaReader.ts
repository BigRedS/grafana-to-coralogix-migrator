import type { Locator, Page } from "@playwright/test";
import type { DashboardSnapshot } from "./types";

const GRAFANA_HOST_URL = "https://watcher.coralogix.com/#/grafana";
const NO_DATA_PATTERN = /no data|no results/i;
const ERROR_PATTERN = /query failed|panel rendering error|runtime error|exception|request failed/i;
const FRAME_SELECTORS = [
  "iframe.grafana-iframe",
  "main iframe.grafana-iframe",
  "main iframe[src*='grafana']",
  "iframe[src*='grafana']",
  "iframe[title*='Grafana']",
  "iframe[data-testid*='grafana']"
];

export async function readGrafanaDashboard(page: Page, grafanaDashboardId: string): Promise<DashboardSnapshot> {
  const dashboardUrl = `${GRAFANA_HOST_URL}/d/${encodeURIComponent(grafanaDashboardId)}`;
  await page.goto(dashboardUrl, { waitUntil: "domcontentloaded" });
  await page.waitForLoadState("load");
  await Promise.race([
    page.waitForSelector("iframe.grafana-iframe, iframe[src*='grafana']", { timeout: 8_000 }),
    page.waitForSelector("[data-testid='Panel'], .panel-container, [data-testid^='Panel header ']", { timeout: 8_000 })
  ]).catch(() => undefined);
  await page.waitForTimeout(800);

  const frameRoot = await getGrafanaFrameRoot(page);
  const dashboardName =
    ((await frameRoot.locator("[data-testid$='breadcrumb']").last().textContent().catch(() => "")) ?? "").trim()
    || ((await frameRoot.locator("h1").first().textContent().catch(() => "")) ?? "").trim()
    || grafanaDashboardId;

  const panels = await frameRoot.evaluate(
    (body, patterns) => {
      const headerNodes = Array.from(
        body.querySelectorAll("[data-testid^='Panel header '], .panel-title, h1, h2, h3, h4, h5, h6")
      );
      const map = new Map();
      let index = 0;

      for (const header of headerNodes) {
        const headerText = (header.textContent || "").trim();
        if (!headerText)
          continue;
        if (headerText.length > 120)
          continue;
        const panelRoot =
          header.closest("[data-testid='Panel'], .panel-container, article, section, [class*='panel']")
          || header.parentElement?.parentElement
          || header.parentElement
          || header;
        const rawText = (panelRoot.textContent || "").trim();
        if (!rawText)
          continue;
        const id = panelRoot.getAttribute("data-testid") || `grafana-panel-${++index}`;
        const key = `${id}-${headerText}`;
        if (map.has(key))
          continue;

        map.set(key, {
          panelId: id,
          panelName: headerText.replace(/^Panel header\s+/i, "").trim(),
          rawText
        });
      }

      return Array.from(map.values()).map((panel) => ({
        ...panel,
        numericValues: parseNumericValuesInPage(panel.rawText),
        hasNoData: patterns.noData.test(panel.rawText),
        hasVisibleError: patterns.error.test(panel.rawText)
      }));

      function parseNumericValuesInPage(value) {
        const normalized = value.replace(/,/g, "");
        const matches = normalized.match(/-?\d+(\.\d+)?/g) || [];
        const numbers = matches.map((raw) => Number(raw)).filter((parsed) => Number.isFinite(parsed));
        return [...new Set(numbers)];
      }
    },
    { noData: NO_DATA_PATTERN, error: ERROR_PATTERN }
  ) as DashboardSnapshot["panels"];

  return {
    pageKind: "grafana",
    dashboardName,
    url: dashboardUrl,
    panels
  };
}

export async function getGrafanaFrameRoot(page: Page): Promise<Locator> {
  const inlinePanels = page.locator("[data-testid='Panel'], .panel-container, [data-testid^='Panel header ']");
  if ((await inlinePanels.count()) > 0) {
    return page.locator("body").first();
  }

  for (const selector of FRAME_SELECTORS) {
    const frameElements = page.locator(selector);
    const frameCount = await frameElements.count();
    for (let index = 0; index < frameCount; index += 1) {
      const frameElement = frameElements.nth(index);
      const src = (await frameElement.getAttribute("src").catch(() => "")) ?? "";
      if (src && !/grafana/i.test(src))
        continue;

      const frameHandle = await frameElement.elementHandle();
      const frame = frameHandle ? await frameHandle.contentFrame() : null;
      if (!frame)
        continue;

      const body = frame.locator("body");
      if ((await body.count()) === 0)
        continue;

      await body.first().waitFor({ state: "visible", timeout: 5_000 }).catch(() => undefined);
      return body.first();
    }
  }

  throw new Error("Unable to locate embedded Grafana iframe under /#/grafana.");
}
