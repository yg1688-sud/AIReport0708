// AIExport API 调用封装
const API_BASE = 'http://localhost:5000/api';

async function request(path, options = {}) {
  const token = localStorage.getItem('token');
  const headers = { ...options.headers };
  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (!(options.body instanceof FormData)) {
    headers['Content-Type'] = 'application/json';
  }

  const res = await fetch(`${API_BASE}${path}`, { ...options, headers });

  if (res.status === 401) {
    localStorage.removeItem('token');
    window.location.href = '/pages/login.html';
    throw new Error('会话已过期，请重新登录');
  }

  if (res.status === 410) {
    window.dispatchEvent(new CustomEvent('session-timeout'));
    throw new Error('会话已超时，请重新上传文件开始');
  }

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: { message: '请求失败' } }));
    throw new Error(err.error?.message || `HTTP ${res.status}`);
  }

  return res;
}

export async function get(path) { return request(path); }
export async function post(path, body) {
  return request(path, { method: 'POST', body: body instanceof FormData ? body : JSON.stringify(body) });
}
export async function put(path, body) {
  return request(path, { method: 'PUT', body: JSON.stringify(body) });
}
export async function del(path) {
  return request(path, { method: 'DELETE' });
}
