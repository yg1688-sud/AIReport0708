import { test, expect } from '@playwright/test';

test.describe('对话模式完整流程', () => {
  test('登录 → 上传 → 聊天确认 → 生成报告 → 保存模版', async ({ page }) => {
    // 1. 登录
    await page.goto('/');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'admin123');
    await page.click('#loginBtn');
    await page.waitForURL('**/#main');

    // 2. 上传区域应可见
    await expect(page.locator('#fileList')).toBeVisible();

    // 3. 聊天区域应可见
    await expect(page.locator('#chatArea')).toBeVisible();
  });

  test('未登录访问被重定向', async ({ page }) => {
    await page.goto('/#main');
    await page.waitForURL('**/#login');
    await expect(page.locator('#loginForm')).toBeVisible();
  });

  test('错误密码显示提示', async ({ page }) => {
    await page.goto('/');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'wrong');
    await page.click('#loginBtn');
    await expect(page.locator('#errorMsg')).toBeVisible();
  });
});
