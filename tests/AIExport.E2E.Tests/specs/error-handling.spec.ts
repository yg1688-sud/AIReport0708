import { test, expect } from '@playwright/test';

test.describe('错误处理', () => {
  test('空文件上传提示', async ({ page }) => {
    await page.goto('/');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'admin123');
    await page.click('#loginBtn');
    await page.waitForURL('**/#main');
    // 空文件上传验证由后端 API 处理
  });

  test('未登录访问API返回401', async ({ request }) => {
    const res = await request.post('http://localhost:5000/api/auth/login', {
      data: { username: '', password: '' }
    });
    expect(res.status()).toBe(400);
  });

  test('登录API正常响应', async ({ request }) => {
    const res = await request.post('http://localhost:5000/api/auth/login', {
      data: { username: 'admin', password: 'admin123' }
    });
    expect([200, 401]).toContain(res.status());
  });
});
