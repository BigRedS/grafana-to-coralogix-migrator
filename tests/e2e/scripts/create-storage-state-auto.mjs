import fs from "node:fs";
import path from "node:path";
import { chromium } from "@playwright/test";

const email = process.argv[2];
const password = process.argv[3];

if (!email || !password) {
  console.error("Usage: node tests/e2e/scripts/create-storage-state-auto.mjs <email> <password>");
  process.exit(1);
}

const authDir = path.resolve(process.cwd(), "tests/e2e/.auth");
const storageStatePath = path.join(authDir, "storage-state.json");

async function run() {
  fs.mkdirSync(authDir, { recursive: true });

  const browser = await chromium.launch({ headless: false, slowMo: 75 });
  const context = await browser.newContext();
  const page = await context.newPage();

  await page.goto("https://watcher.coralogix.com/#/login/user", { waitUntil: "domcontentloaded" });
  await page.locator("[data-test='login-username-input'] [data-test='cx-input-field']").fill(email);
  await page.locator("[data-test='login-password-input'] [data-test='cx-input-field']").fill(password);
  await page.locator("[data-test='login-sign-in-btn'] [data-test='cx-button']").click();
  await page.waitForSelector("[data-test='shell-sidenav'], [data-test='custom-dashboards-container']", {
    timeout: 45000
  });

  await context.storageState({ path: storageStatePath });
  await browser.close();
  console.log(`Saved storage state to ${storageStatePath}`);
}

run().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
