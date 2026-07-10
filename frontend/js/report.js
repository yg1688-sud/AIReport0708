// AIExport 报告模块
import { get, post } from './api.js';

let currentReportId = null;

// ===== US6: 报告生成 =====
export function subscribeReportProgress(taskId) {
  const eventSource = new EventSource(`http://localhost:5000/api/reports/progress?taskId=${taskId}`);
  const progressBar = document.getElementById('reportProgressBar');
  const stageText = document.getElementById('reportStageText');

  eventSource.addEventListener('progress', (e) => {
    const data = JSON.parse(e.data);
    if (progressBar) progressBar.style.width = data.Percent + '%';
    if (stageText) stageText.textContent = data.Message;
    document.getElementById('reportProgressArea').style.display = 'block';

    if (data.Stage === 'complete') {
      currentReportId = taskId;
      eventSource.close();
      document.getElementById('reportProgressArea').style.display = 'none';
      loadAndRenderReport(taskId);
    }
  });

  eventSource.onerror = () => { eventSource.close(); };
}

async function loadAndRenderReport(reportId) {
  const res = await get(`/reports/${reportId}`);
  const report = await res.json();
  renderReport(report);
  showPostReportActions(report);
}

export function renderReport(report) {
  const container = document.getElementById('reportContent');
  if (!container) return;
  container.style.display = 'block';

  let chapters;
  try { chapters = JSON.parse(report.chapters || '{}'); } catch { chapters = {}; }
  const overview = chapters.overview || {};
  const stats = chapters.statistics || [];

  container.innerHTML = `
    <div class="card"><h2>一、数据概览</h2>
      <p>总行数：${overview.rowCount?.toLocaleString() || '-'} | 总列数：${overview.columnCount || '-'} | 缺失率：${((overview.missingRate || 0) * 100).toFixed(1)}%</p>
      <p>数据文件：${overview.fileName || '-'}</p>
    </div>
    <div class="card"><h2>二、描述性统计</h2>
      <table style="width:100%;border-collapse:collapse;">
        <tr style="border-bottom:2px solid #e8e8e8;"><th style="padding:8px;">列名</th><th>均值</th><th>中位数</th><th>最小值</th><th>最大值</th><th>标准差</th></tr>
        ${stats.map(s => `<tr style="border-bottom:1px solid #f0f0f0;"><td style="padding:8px;">${s.ColumnName}</td><td>${s.Mean}</td><td>${s.Median}</td><td>${s.Min}</td><td>${s.Max}</td><td>${s.StdDev}</td></tr>`).join('')}
      </table>
    </div>
    <div class="card"><h2>三、图表分析</h2><p>（图表详情在网页端查看）</p></div>
    <div class="card"><h2>四、交叉分析</h2><p>${chapters.crossAnalysis?.length ? chapters.crossAnalysis.join('; ') : '无交叉分析数据'}</p></div>`;
}

function showPostReportActions(report) {
  document.getElementById('downloadPrintArea').style.display = 'block';
  document.getElementById('saveTemplatePrompt').style.display =
    report.mode === 'chat' ? 'block' : 'none';
}

// ===== 模版保存提示 (US6 scenario 6-7) =====
export function promptSaveTemplate() {
  document.getElementById('saveTemplatePrompt').style.display = 'block';
}

window._saveTemplate = async () => {
  const name = document.getElementById('templateNameInput')?.value?.trim();
  if (!name) { alert('模版名称不能为空'); return; }
  // Import chat.js to get sessionId
  const { getCurrentSessionId } = await import('./chat.js');
  await post('/templates', { sessionId: getCurrentSessionId(), name });
  document.getElementById('saveTemplatePrompt').style.display = 'none';
  alert('模版保存成功');
};

// ===== US7: 下载 & 打印 =====
export async function downloadReport(reportId) {
  const token = localStorage.getItem('token');
  window.open(`http://localhost:5000/api/reports/${reportId || currentReportId}/download?token=${token}`, '_blank');
}

export function printReport(reportId) {
  window.print();
}

// ===== US8: 历史报告管理 =====
export async function loadHistory(params = {}) {
  const qs = new URLSearchParams(params).toString();
  const res = await get(`/reports?${qs}`);
  return await res.json();
}

export async function deleteReport(id) {
  await fetch(`http://localhost:5000/api/reports/${id}`, {
    method: 'DELETE',
    headers: { 'Authorization': `Bearer ${localStorage.getItem('token')}` }
  });
}

export async function batchDownload(ids) {
  const res = await post('/reports/batch-download', { reportIds: ids });
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = 'reports.zip'; a.click();
  URL.revokeObjectURL(url);
}
