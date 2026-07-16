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
        window._resetAfterReport?.();
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
    ${chapters.summaryMetrics?.length ? '<div class="card"><h2>汇总指标</h2><table class="data-table" style="border:1px solid #e8e8e8;width:100%;"><tbody>' + chapters.summaryMetrics.map(m => { const i = m.indexOf('='); return '<tr><td style="padding:8px 16px;"><strong>' + m.substring(0,i) + '</strong></td><td style="padding:8px 16px;">' + m.substring(i+1) + '</td></tr>'; }).join('') + '</tbody></table></div>' : ''}
    ${chapters.computedResults?.length ? (() => {
      // 从 meta 行 "分组列: 片区 | ..." 提取实际分组列名作为首列表头
      const metaLine = chapters.computedResults.find(r => r.startsWith('分组列:'));
      const groupColName = metaLine ? metaLine.split('|')[0].replace('分组列:', '').trim() : '分组';
      const allRows = chapters.computedResults.filter(r => !r.startsWith('分组列:') && r !== '---');
      if (allRows.length === 0) return '';

      // 从第一行数据自动提取列名
      const first = allRows[0];
      let headers = [];
      let isGrouped = false;

      // 检查是否有冒号（分组格式 "片区: val1=xxx, val2=yyy"）还是纯多列格式 "col1=xxx, col2=yyy"
      const colonIdx = first.indexOf(':');
      const segments = first.split(',');

      if (colonIdx > 0 && first.substring(colonIdx + 1).includes('=')) {
        // 分组格式: key: col1=v1, col2=v2 — 只从冒号后的部分提取列名，避免混入分组键
        isGrouped = true;
        headers.push(groupColName);
        const afterColon = first.substring(colonIdx + 1).trim();
        afterColon.split(',').forEach(p => {
          const eq = p.indexOf('=');
          if (eq > 0) headers.push(p.substring(0, eq).trim());
        });
      } else {
        // 纯多列格式: col1=v1, col2=v2
        headers = segments.map(p => {
          const eq = p.indexOf('=');
          return eq > 0 ? p.substring(0, eq).trim() : p.trim();
        }).filter(Boolean);
      }

      if (headers.length === 0) headers = ['指标', '数值'];

      // 存储表格数据供导出使用
      const tableRows = [];
      allRows.forEach(r => {
        const ci = r.indexOf(':');
        if (isGrouped && ci > 0) {
          const key = r.substring(0, ci).trim();
          const rest = r.substring(ci + 1).trim();
          const vals = rest.split(',').map(p => {
            const eq = p.indexOf('=');
            return eq > 0 ? p.substring(eq + 1).trim() : p.trim();
          });
          tableRows.push([key, ...vals]);
        } else {
          const vals = r.split(',').map(p => {
            const eq = p.indexOf('=');
            return eq > 0 ? p.substring(eq + 1).trim() : p.trim();
          });
          tableRows.push(vals);
        }
      });
      window._reportTableData = { headers, rows: tableRows, title: report.originalFileName || '分析报告', summaryMetrics: chapters.summaryMetrics };

      return '<div class="card"><h2>计算结果</h2><table class="data-table" style="border:1px solid #e8e8e8;width:100%;"><thead><tr>' + headers.map(h => '<th>' + h + '</th>').join('') + '</tr></thead><tbody>' + allRows.map(r => {
        const ci = r.indexOf(':');
        if (isGrouped && ci > 0) {
          // 分组行：提取 key 和 values
          const key = r.substring(0, ci).trim();
          const rest = r.substring(ci + 1).trim();
          const vals = rest.split(',').map(p => {
            const eq = p.indexOf('=');
            return eq > 0 ? p.substring(eq + 1).trim() : p.trim();
          });
          return '<tr><td><strong>' + key + '</strong></td>' + vals.map(v => '<td>' + v + '</td>').join('') + '</tr>';
        } else {
          // 非分组行：每段是 key=value
          const vals = r.split(',').map(p => {
            const eq = p.indexOf('=');
            return eq > 0 ? p.substring(eq + 1).trim() : p.trim();
          });
          return '<tr>' + vals.map(v => '<td>' + v + '</td>').join('') + '</tr>';
        }
      }).join('') + '</tbody></table></div>';
    })() : ''}
    ${!chapters.computedResults?.length && !chapters.crossAnalysis?.length && !chapters.customRequirements ? '<div class="card"><h2>数据概览</h2><p>总行数：' + (overview.rowCount?.toLocaleString() || '-') + ' | 总列数：' + (overview.columnCount || '-') + '</p><p>文件：' + (overview.fileName || '-') + '</p></div>' : ''}
    ${!chapters.computedResults?.length && !chapters.crossAnalysis?.length && !chapters.customRequirements && stats.length ? '<div class="card"><h2>描述性统计</h2><table class="data-table" style="border:1px solid #e8e8e8;"><thead><tr><th>列名</th><th>均值</th><th>中位数</th><th>最小值</th><th>最大值</th></tr></thead><tbody>' + stats.map(s => '<tr><td><strong>' + (s.ColumnName || '') + '</strong></td><td>' + (s.Mean || 0) + '</td><td>' + (s.Median || 0) + '</td><td>' + (s.Min || 0) + '</td><td>' + (s.Max || 0) + '</td></tr>').join('') + '</tbody></table></div>' : ''}
    ${chapters.crossAnalysis?.length ? '<div class="card"><h2>分组统计结果</h2><table class="data-table" style="border:1px solid #e8e8e8;"><thead><tr><th>分组</th><th>结果</th></tr></thead><tbody>' + chapters.crossAnalysis.map(c => '<tr><td>' + c.split(':')[0] + '</td><td>' + c.split(':').slice(1).join(':') + '</td></tr>').join('') + '</tbody></table></div>' : ''}`;

  showPostReportActions(report);
}

function showPostReportActions(report) {
  const dp = document.getElementById('downloadPrintArea');
  if (dp) dp.style.display = 'block';
  const sp = document.getElementById('saveTemplatePrompt');
  if (sp) {
    const isChatMode = report.mode === 0 || report.mode === 'chat';
    sp.style.display = isChatMode ? 'block' : 'none';
  }
}

export function promptSaveTemplate() {
  document.getElementById('saveTemplatePrompt').style.display = 'block';
}

window._saveTemplate = async () => {
  const name = document.getElementById('templateNameInput')?.value?.trim();
  if (!name) { alert('模版名称不能为空'); return; }
  const { getCurrentSessionId } = await import('./chat.js');
  await post('/templates', { sessionId: getCurrentSessionId(), name });
  document.getElementById('saveTemplatePrompt').style.display = 'none';
  alert('模版保存成功');
};

export async function downloadReport(reportId) {
  const id = reportId || currentReportId;
  const token = localStorage.getItem('token');
  const a = document.createElement('a');
  a.href = `http://localhost:5000/api/reports/${id}/download`;
  const res = await fetch(a.href, { headers: { Authorization: `Bearer ${token}` } });
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  a.href = url; a.download = `report-${id}.pdf`; a.click();
  URL.revokeObjectURL(url);
}

export function printReport(reportId) {
  window.print();
}

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

// ===== 导出 Excel =====
export function exportExcel() {
  const data = window._reportTableData;
  if (!data || !data.rows || data.rows.length === 0) {
    alert('暂无计算结果可导出');
    return;
  }

  // 构建 SheetJS 工作表：第一行为表头，末尾追加汇总指标
  const sheetData = [data.headers, ...data.rows];
  if (data.summaryMetrics?.length) {
    sheetData.push([]); // 空行分隔
    sheetData.push(['汇总指标', '']);
    data.summaryMetrics.forEach(m => {
      const i = m.indexOf('=');
      sheetData.push([i > 0 ? m.substring(0, i) : m, i > 0 ? m.substring(i + 1) : '']);
    });
  }
  const ws = XLSX.utils.aoa_to_sheet(sheetData);

  // 自动列宽
  const colWidths = data.headers.map((h, i) => {
    let maxLen = h.length;
    data.rows.forEach(r => {
      const v = String(r[i] ?? '');
      // 中文字符按2个宽度计算
      const len = [...v].reduce((acc, ch) => acc + (ch.charCodeAt(0) > 127 ? 2 : 1), 0);
      if (len > maxLen) maxLen = len;
    });
    return { wch: Math.min(maxLen + 3, 40) };
  });
  ws['!cols'] = colWidths;

  const wb = XLSX.utils.book_new();
  const sheetName = (data.title || '计算结果').substring(0, 31); // Excel sheet名最长31字符
  XLSX.utils.book_append_sheet(wb, ws, sheetName);

  const filename = (data.title || 'report') + '.xlsx';
  XLSX.writeFile(wb, filename);
}
