import { test, expect } from '@playwright/test';

const API = 'http://localhost:5000/api';

test.describe('前端页面', () => {
  test('登录页面正常加载', async ({ page }) => {
    await page.goto('/pages/login.html');
    await expect(page.locator('#loginForm')).toBeVisible();
    await expect(page.locator('#username')).toBeVisible();
    await expect(page.locator('#password')).toBeVisible();
    await expect(page.locator('#loginBtn')).toBeVisible();
  });

  test('管理员登录后跳转到主页', async ({ page }) => {
    await page.goto('/pages/login.html');
    await page.fill('#username', 'admin');
    await page.fill('#password', 'admin123');
    await page.click('#loginBtn');
    await page.waitForURL('**/pages/main.html**', { timeout: 10000 });
    expect(page.url()).toContain('main.html');
  });
});

test.describe('后端API', () => {
  test('健康检查', async ({ request }) => {
    const res = await request.get(`${API}/health`);
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.status).toBe('ok');
  });

  test('登录 - 正确凭据返回JWT', async ({ request }) => {
    const res = await request.post(`${API}/auth/login`, {
      data: { username: 'admin', password: 'admin123' }
    });
    expect(res.status()).toBe(200);
    const body = await res.json();
    expect(body.token).toBeTruthy();
    expect(body.user.username).toBe('admin');
  });

  test('登录 - 错误密码返回401', async ({ request }) => {
    const res = await request.post(`${API}/auth/login`, {
      data: { username: 'admin', password: 'wrongpassword' }
    });
    expect(res.status()).toBe(401);
  });

  test('未认证访问受保护端点返回401', async ({ request }) => {
    const res = await request.get(`${API}/templates`);
    expect(res.status()).toBe(401);
  });

  test('带Token访问模版列表返回200', async ({ request }) => {
    const loginRes = await request.post(`${API}/auth/login`, {
      data: { username: 'admin', password: 'admin123' }
    });
    const { token } = await loginRes.json();
    const res = await request.get(`${API}/templates`, {
      headers: { Authorization: `Bearer ${token}` }
    });
    expect(res.status()).toBe(200);
  });
});
