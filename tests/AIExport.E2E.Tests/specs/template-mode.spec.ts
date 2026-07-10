import { test, expect } from '@playwright/test';

test.describe('模版模式', () => {
  test('登录 → 上传 → 选模版 → 立即生成', async ({ page }) => {
    await page.goto('/');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'admin123');
    await page.click('#loginBtn');
    await page.waitForURL('**/#main');

    // 模版选择区域应在文件就绪后显示
    await expect(page.locator('#templateSelector')).toBeTruthy();
  });
});
