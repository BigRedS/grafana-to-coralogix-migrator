import type { Locator } from "@playwright/test";

const ERROR_TEXT_MARKERS = [
  "query failed",
  "panel rendering error",
  "runtime error",
  "request failed",
  "failed to load",
  "cannot be empty",
  "we couldn't retrieve data",
  "exception"
];

export async function findVisibleErrorMarkers(root: Locator): Promise<string[]> {
  const selectors = [
    "[role='alert']",
    "[data-testid*='error']",
    ".panel-alert",
    ".alert-error",
    "[data-test*='error']"
  ];

  const markers = new Set<string>();

  for (const selector of selectors) {
    const nodes = root.locator(selector);
    const count = await nodes.count();
    for (let index = 0; index < count; index += 1) {
      const node = nodes.nth(index);
      if (!(await node.isVisible().catch(() => false))) {
        continue;
      }
      const text = ((await node.innerText().catch(() => "")) ?? "").trim();
      if (text && looksLikeError(text)) {
        markers.add(text);
      }
    }
  }

  for (const marker of ERROR_TEXT_MARKERS) {
    const nodes = root.getByText(new RegExp(marker, "i"));
    const count = await nodes.count();
    for (let index = 0; index < count; index += 1) {
      const node = nodes.nth(index);
      if (!(await node.isVisible().catch(() => false))) {
        continue;
      }
      const text = ((await node.innerText().catch(() => "")) ?? "").trim();
      if (text) {
        markers.add(text);
      }
    }
  }

  return [...markers];
}

function looksLikeError(text: string): boolean {
  return /(query failed|panel rendering error|runtime error|request failed|failed to load|cannot be empty|couldn't retrieve data|exception)/i.test(text);
}
