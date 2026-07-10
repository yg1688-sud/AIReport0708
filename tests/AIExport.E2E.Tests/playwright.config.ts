import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  timeout: 120000,
  retries: 1,
  use: {
    baseURL: 'http://localhost:3000',
    browserName: 'chromium',
    viewport: { width: 1920, height: 1080 },
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1920, height: 1080 } } },
    { name: 'tablet', use: { viewport: { width: 768, height: 1024 } } },
  ],
});
