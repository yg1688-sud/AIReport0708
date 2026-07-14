// AIExport 报告模块
import { get, post } from './api.js';

let currentReportId = null;
window._currentReportId = null;

// ===== US6: 报告生成 =====
let reportPollInterval = null;

export function subscribeReportProgress(taskId) {
  if (reportPollInterval) clearInterval(reportPollInterval);

  document.getElementById('reportProgressArea').style.display = 'block';
  const progressBar = document.getElementById('reportProgressBar');
  const stageText = document.getElementById('reportStageText');

  reportPollInterval = setInterval(async () => {
    try {
      const res = await fetch(`http://localhost:5000/api/reports/progress?taskId=${taskId}`);
      const data = await res.json();

      if (progressBar) progressBar.style.width = data.percent + '%';
      if (stageText) stageText.textContent = data.message || data.stage;

      if (data.completed) {
        clearInterval(reportPollInterval);
        currentReportId = taskId;
        window._currentReportId = taskId;
        document.getElementById('reportProgressArea').style.display = 'none';
        loadAndRenderReport(taskId);
      }
    } catch (e) {
      console.error('Report poll error:', e);
    }
  }, 1000);
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
    ${chapters.customRequirements ? '<div class="card"><h2>确认的分析需求</h2><p style="white-space:pre-wrap;color:#666;">' + chapters.customRequirements + '</p></div>' : ''}
    ${chapters.computedResults?.length ? '<div class="card"><h2>计算结果</h2><table class="data-table" style="border:1px solid #e8e8e8;width:100%;"><thead><tr><th>指标</th><th>数值</th></tr></thead><tbody>' + chapters.computedResults.map(r => { const idx = r.indexOf(':'); return '<tr><td style="padding:10px;"><strong>' + r.substring(0,idx) + '</strong></td><td style="padding:10px;font-size:16px;">' + r.substring(idx+1) + '</td></tr>'; }).join('') + '</tbody></table></div>' : ''}
    ${!chapters.computedResults?.length && !chapters.crossAnalysis?.length ? '<div class="card"><h2>数据概览</h2><p>总行数：' + (overview.rowCount?.toLocaleString() || '-') + ' | 总列数：' + (overview.columnCount || '-') + '</p><p>文件：' + (overview.fileName || '-') + '</p></div>' : ''}
    ${!chapters.computedResults?.length && !chapters.crossAnalysis?.length && stats.length ? '<div class="card"><h2>描述性统计</h2><table class="data-table" style="border:1px solid #e8e8e8;"><thead><tr><th>列名</th><th>均值</th><th>中位数</th><th>最小值</th><th>最大值</th></tr></thead><tbody>' + stats.map(s => '<tr><td><strong>' + (s.ColumnName || '') + '</strong></td><td>' + (s.Mean || 0) + '</td><td>' + (s.Median || 0) + '</td><td>' + (s.Min || 0) + '</td><td>' + (s.Max || 0) + '</td></tr>').join('') + '</tbody></table></div>' : ''}
    ${chapters.crossAnalysis?.length ? '<div class="card"><h2>分组统计结果</h2><table class="data-table" style="border:1px solid #e8e8e8;"><thead><tr><th>分组</th><th>结果</th></tr></thead><tbody>' + chapters.crossAnalysis.map(c => '<tr><td>' + c.split(':')[0] + '</td><td>' + c.split(':').slice(1).join(':') + '</td></tr>').join('') + '</tbody></table></div>' : ''}`;

  showPostReportActions(report);
}

function showPostReportActions(report) {
  document.getElementById('downloadPrintArea').style.display = 'block';
  // mode: 0=chat, 1=template. 对话模式才提示保存模版
  const isChatMode = report.mode === 0 || report.mode === 'chat';
  document.getElementById('saveTemplatePrompt').style.display = isChatMode ? 'block' : 'none';
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
  const id = reportId || currentReportId;
  const token = localStorage.getItem('token');
  const a = document.createElement('a');
  a.href = `http://localhost:5000/api/reports/${id}/download`;
  // 通过 fetch + blob 下载（支持 Authorization 头）
  const res = await fetch(a.href, { headers: { Authorization: `Bearer ${token}` } });
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  a.href = url; a.download = `report-${id}.pdf`; a.click();
  URL.revokeObjectURL(url);
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
