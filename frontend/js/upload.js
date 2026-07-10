// AIExport 文件上传模块
import { post, get, del } from './api.js';
import { formatFileSize } from './utils.js';

let currentBatchId = null;
let progressSubscriber = null;

export function getCurrentBatchId() { return currentBatchId; }

export async function uploadFiles(fileList) {
  const formData = new FormData();
  for (const file of fileList) formData.append('files', file);

  const res = await post('/files/upload', formData);
  const data = await res.json();
  currentBatchId = data.batchId;
  return data;
}

export function renderFileList(uploadResponse) {
  const container = document.getElementById('fileList');
  if (!container) return;

  container.innerHTML = uploadResponse.files.map(f => `
    <div class="file-item" id="file-${f.id}" style="display:flex;align-items:center;justify-content:space-between;padding:12px;border:1px solid #e8e8e8;border-radius:6px;margin-bottom:8px;">
      <div style="flex:1;">
        <div style="font-weight:500;">${f.originalName}</div>
        <div style="color:#888;font-size:12px;">${formatFileSize(f.fileSize)} · ${f.fileFormat.toUpperCase()} · <span id="status-${f.id}">解析中...</span></div>
      </div>
      <div style="width:200px;">
        <div class="progress-bar"><div id="progress-${f.id}" class="fill" style="width:0%"></div></div>
      </div>
      <button onclick="window._removeFile('${f.id}')" style="margin-left:12px;border:none;background:none;color:#ff4d4f;cursor:pointer;font-size:18px;">×</button>
    </div>
  `).join('');

  // 显示整体进度
  document.getElementById('overallProgress').textContent =
    `0/${uploadResponse.files.length} 个文件已就绪`;
}

export function subscribeProgress(batchId) {
  if (progressSubscriber) clearInterval(progressSubscriber);

  const eventSource = new EventSource(`http://localhost:5000/api/files/progress?batchId=${batchId}`);
  eventSource.addEventListener('batch-progress', (e) => {
    const data = JSON.parse(e.data);
    document.getElementById('overallProgress').textContent =
      `${data.readyFiles}/${data.totalFiles} 个文件已就绪`;

    updateProgressBar(data.readyFiles, data.totalFiles);
  });

  eventSource.addEventListener('batch-ready', (e) => {
    const data = JSON.parse(e.data);
    document.getElementById('overallProgress').textContent = '所有文件就绪';
    updateProgressBar(data.totalFiles, data.totalFiles);
    eventSource.close();
    window.dispatchEvent(new CustomEvent('batch-ready', { detail: data }));
  });

  eventSource.onerror = () => { eventSource.close(); };
  progressSubscriber = eventSource;
}

function updateProgressBar(ready, total) {
  const pct = total > 0 ? Math.round((ready / total) * 100) : 0;
  const bar = document.getElementById('overallProgressBar');
  if (bar) bar.style.width = pct + '%';
}

export async function removeFile(fileId) {
  await del(`/files/${fileId}`);
  const el = document.getElementById(`file-${fileId}`);
  if (el) el.remove();
}

export async function handleClearAll() {
  if (!confirm('确定要清空所有文件吗？')) return;
  await del(`/batches/${currentBatchId}/files`);
  const container = document.getElementById('fileList');
  if (container) container.innerHTML = '';
  document.getElementById('overallProgress').textContent = '';
}

// ===== US3: 多文件处理策略选择 =====
let currentStrategy = null;

export function getCurrentStrategy() { return currentStrategy; }

export async function setStrategy(strategy) {
  currentStrategy = strategy;
  const batchId = getCurrentBatchId();

  const res = await post(`/batches/${batchId}/strategy`, { strategy });
  const data = await res.json();

  if (strategy === 'merge' && !data.isConsistent) {
    showConsistencyWarning(data.differences);
    return false;
  }

  hideStrategySelector();
  window.dispatchEvent(new CustomEvent('strategy-selected', { detail: { strategy } }));
  return true;
}

function showConsistencyWarning(differences) {
  const msg = differences.map(d =>
    `文件 "${d.fileName}" 列不一致：缺少 [${d.missing.join(', ')}]，多余 [${d.extra.join(', ')}]`
  ).join('\n');

  alert(`文件列结构不一致，无法合并分析：\n${msg}\n\n请选择"分别分析"或调整文件后重新上传。`);
}

function hideStrategySelector() {
  const el = document.getElementById('strategySelector');
  if (el) el.style.display = 'none';
}

export function showStrategySelector(fileCount) {
  if (fileCount <= 1) {
    window.dispatchEvent(new CustomEvent('strategy-selected', { detail: { strategy: 'separate' } }));
    return;
  }

  const container = document.getElementById('strategySelector');
  if (!container) return;
  container.style.display = 'block';
  container.innerHTML = `
    <div class="card" style="margin-top:16px;">
      <h3 style="margin-bottom:12px;">检测到多个文件，请选择分析策略：</h3>
      <div style="display:flex;gap:12px;">
        <button onclick="window._selectStrategy('merge')" style="flex:1;padding:16px;border:2px solid #d9d9d9;border-radius:8px;background:#fff;cursor:pointer;font-size:14px;">
          <strong>合并分析</strong><br><small style="color:#888;">所有文件合并为一个报告（列结构需一致）</small>
        </button>
        <button onclick="window._selectStrategy('separate')" style="flex:1;padding:16px;border:2px solid #d9d9d9;border-radius:8px;background:#fff;cursor:pointer;font-size:14px;">
          <strong>分别分析</strong><br><small style="color:#888;">每个文件独立生成报告</small>
        </button>
      </div>
    </div>`;
}

window._selectStrategy = setStrategy;
window._removeFile = removeFile;

