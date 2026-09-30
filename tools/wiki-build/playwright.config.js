const { defineConfig } = require("@playwright/test");
module.exports = defineConfig({
  testDir: "./browser",
  fullyParallel: true,
  workers: 2,
  use: { baseURL: "http://127.0.0.1:4173", browserName: "chromium" },
  webServer: {
    command: "node serve.js",
    url: "http://127.0.0.1:4173/wiki/en/",
    reuseExistingServer: !process.env.CI,
  },
});
