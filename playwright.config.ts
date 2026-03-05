import { defineConfig } from "@playwright/test";

const storageStatePath = "tests/e2e/.auth/storage-state.json";

export default defineConfig({
  testDir: "./tests/e2e",
  timeout: 90_000,
  expect: {
    timeout: 10_000
  },
  use: {
    baseURL: "https://watcher.coralogix.com",
    storageState: storageStatePath,
    trace: "on-first-retry",
    screenshot: "only-on-failure"
  },
  projects: [
    {
      name: "setup",
      testMatch: /auth\.setup\.ts/,
      use: {
        storageState: undefined
      }
    },
    {
      name: "migration",
      testMatch: /migration-compare\.spec\.ts/,
      dependencies: ["setup"]
    }
  ]
});
