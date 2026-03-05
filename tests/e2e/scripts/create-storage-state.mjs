import fs from "node:fs";
import path from "node:path";
import readline from "node:readline";
import { chromium } from "@playwright/test";

const AUTH_DIR = path.resolve(process.cwd(), "tests/e2e/.auth");
const STORAGE_STATE_PATH = path.join(AUTH_DIR, "storage-state.json");

async function main() {
  fs.mkdirSync(AUTH_DIR, { recursive: true });

  const browser = await chromium.launch({ headless: false, slowMo: 150 });
  const context = await browser.newContext();
  const page = await context.newPage();

  await page.goto("https://watcher.coralogix.com/#/dashboards", { waitUntil: "domcontentloaded" });

  process.stdout.write("\nComplete the login flow in the opened browser window.\n");
  process.stdout.write("Press Enter here after the target session is fully authenticated...\n");
  await waitForEnter();

  await context.storageState({ path: STORAGE_STATE_PATH });
  await browser.close();

  process.stdout.write(`Saved storage state to ${STORAGE_STATE_PATH}\n`);
}

function waitForEnter() {
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout
  });

  return new Promise((resolve) => {
    rl.question("", () => {
      rl.close();
      resolve();
    });
  });
}

main().catch((error) => {
  process.stderr.write(`Failed to create storage state: ${error instanceof Error ? error.message : String(error)}\n`);
  process.exit(1);
});
