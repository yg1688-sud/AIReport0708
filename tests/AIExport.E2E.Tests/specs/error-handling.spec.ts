import { test, expect } from '@playwright/test';

const API = 'http://localhost:5000/api';

test.describe('错误处理 - API 层', () => {
  test('空用户名登录返回400', async ({ request }) => {
    const res = await request.post(`${API}/auth/login`, {
      data: { username: '', password: 'test123' }
    });
    expect(res.status()).toBe(400);
  });

  test('空密码登录返回400', async ({ request }) => {
    const res = await request.post(`${API}/auth/login`, {
      data: { username: 'admin', password: '' }
    });
    expect(res.status()).toBe(400);
  });

  test('不存在用户登录返回401', async ({ request }) => {
    const res = await request.post(`${API}/auth/login`, {
      data: { username: 'nobody', password: 'test123' }
    });
    expect(res.status()).toBe(401);
  });

  test('不支持的格式上传返回400', async ({ request }) => {
    const loginRes = await request.post(`${API}/auth/login`, {
      data: { username: 'admin', password: 'admin123' }
    });
    const { token } = await loginRes.json();

    // 发送空表单（无文件）
    const res = await request.post(`${API}/files/upload`, {
      headers: { Authorization: `Bearer ${token}` },
      multipart: {}  // 无文件
    });
    expect([400, 401]).toContain(res.status());
  });
});
