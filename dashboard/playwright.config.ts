import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 1,
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: process.env.DASHBOARD_URL || 'http://localhost:3000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  ...(process.env.CI
    ? {} // In CI, the docker compose stack serves the dashboard directly; no need for webServer
    : {
        webServer: {
          command: 'bun run dev',
          url: 'http://localhost:3000',
          reuseExistingServer: true,
          timeout: 30000,
        },
      }
  ),
})
