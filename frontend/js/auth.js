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
  window.location.hash = '#login';
}

export function checkAuth() {
  const token = getToken();
  if (!token) { window.location.hash = '#login'; return false; }
  return true;
}
