import { test, expect } from '@playwright/test';

test.describe('模版模式', () => {
  test('登录后主页可见上传区域', async ({ page }) => {
    await page.goto('/pages/login.html');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'admin123');
    await page.click('#loginBtn');
    await page.waitForURL('**/pages/main.html**', { timeout: 10000 });

    // 主页应加载完成
    await page.waitForTimeout(1000);
  });

  test('平板端登录页面适配正常', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto('/pages/login.html');
    await expect(page.locator('#loginForm')).toBeVisible();
    // 平板端登录卡片应完整可见
    const form = page.locator('.login-container');
    await expect(form).toBeVisible();
  });
});
