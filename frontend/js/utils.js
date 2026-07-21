// AIExport 通用工具函数

export function formatFileSize(bytes) {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  let i = 0;
  let size = bytes;
  while (size >= 1024 && i < units.length - 1) { size /= 1024; i++; }
  return `${size.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

export function formatDate(iso) {
  if (!iso) return '-';
  // UTC 时间转本地时间（+8 时区）
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')} ${String(d.getHours()).padStart(2,'0')}:${String(d.getMinutes()).padStart(2,'0')}:${String(d.getSeconds()).padStart(2,'0')}`;
}

export function debounce(fn, delay = 300) {
  let timer;
  return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), delay); };
}

// 全局错误边界
window.onerror = function (msg, url, line) {
  console.error('Global error:', msg, url, line);
  const app = document.getElementById('app');
  if (app) {
    app.innerHTML = `<div class="card" style="text-align:center;margin-top:100px;">
      <h2>页面遇到了意外错误</h2>
      <p style="color:#888;">${msg}</p>
      <button onclick="location.reload()" style="margin-top:16px;padding:8px 24px;">刷新页面</button>
      <button onclick="location.hash='#main'" style="margin-top:16px;padding:8px 24px;margin-left:12px;">重新上传</button>
    </div>`;
  }
  return true;
};

window.onunhandledrejection = function (event) {
  console.error('Unhandled rejection:', event.reason);
};
