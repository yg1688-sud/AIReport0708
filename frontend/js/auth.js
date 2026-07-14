// AIExport 认证管理
import { post } from './api.js';

const TOKEN_KEY = 'token';
const USER_KEY = 'user';

export function getToken() { return localStorage.getItem(TOKEN_KEY); }
export function getUser() { return JSON.parse(localStorage.getItem(USER_KEY) || 'null'); }

export async function login(username, password) {
  const res = await post('/auth/login', { username, password });
  const data = await res.json();
  localStorage.setItem(TOKEN_KEY, data.token);
  localStorage.setItem(USER_KEY, JSON.stringify(data.user));
  return data;
}

export function logout() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
  window.location.href = '/pages/login.html';
}

export function checkAuth() {
  const token = getToken();
  if (!token) { window.location.href = '/pages/login.html'; return false; }
  return true;
}

export function isAdmin() {
  const user = getUser();
  return user?.role === 'admin';
}

// 页面加载时自动隐藏管理员菜单（非 admin 用户）
document.addEventListener('DOMContentLoaded', () => {
  if (!isAdmin()) {
    // 隐藏侧边栏中的管理员菜单项
    document.querySelectorAll('.sidebar .menu li').forEach(li => {
      if (li.textContent?.includes('用户管理')) li.style.display = 'none';
    });
  }
});
