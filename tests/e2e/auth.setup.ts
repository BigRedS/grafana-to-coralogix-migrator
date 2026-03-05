import fs from "node:fs";
import path from "node:path";
import { test, expect } from "@playwright/test";

const STORAGE_STATE_PATH = path.resolve(process.cwd(), "tests/e2e/.auth/storage-state.json");

test("storage state exists for E2E migration checks", async () => {
  const exists = fs.existsSync(STORAGE_STATE_PATH);
  expect(
    exists,
    `Missing Playwright storage state at ${STORAGE_STATE_PATH}. Run "npm run e2e:auth" to create it.`
  ).toBe(true);
});
