// AIExport 管理员模块 (Phase 11 / C1)
export async function loadUsers() {
  const { get } = await import('./api.js');
  const res = await get('/admin/users');
  return (await res.json()).users;
}

export async function createUser(data) {
  const { post } = await import('./api.js');
  return post('/admin/users', data);
}

export async function toggleUserStatus(id) {
  const { put } = await import('./api.js');
  return put(`/admin/users/${id}/toggle`, {});
}

export async function deleteUser(id) {
  const { del } = await import('./api.js');
  return del(`/admin/users/${id}`);
}
