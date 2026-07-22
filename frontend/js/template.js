// AIExport 模版模块
import { get, post, del } from './api.js';
import { getCurrentBatchId } from './upload.js';

let selectedTemplateId = null;

export function getSelectedTemplateId() { return selectedTemplateId; }

export async function loadTemplates() {
  const res = await get('/templates');
  const data = await res.json();
  return data.templates;
}

export function renderTemplateSelector(templates, forceTemplate = false) {
  const container = document.getElementById('templateSelector');
  if (!container) return;

  if (!templates || templates.length === 0) {
    container.style.display = 'none';
    window.dispatchEvent(new CustomEvent('template-selected', { detail: { templateId: null } }));
    return;
  }

  container.style.display = 'block';
  const chatOption = forceTemplate ? '' : '<option value="__chat__"> 不使用模版（进入对话模式）</option>';
  container.innerHTML = `
    <div class="card" style="margin-top:16px;">
      <h3 style="margin-bottom:8px;">选择分析模版 ${forceTemplate ? '<span style="color:#ff4d4f;">*多文件必选</span>' : '<span style="color:#ff4d4f;">*必选</span>'}</h3>
      <select id="templateSelect" style="width:100%;padding:8px;border:2px solid #1677ff;border-radius:6px;font-size:14px;">
        <option value="" selected>-- 请选择模版${forceTemplate ? '' : '或对话模式'} --</option>
        ${chatOption}
        ${templates.map(t => `<option value="${t.id}">  ${t.name}（${t.strategy === 'merge' ? '合并' : '分别'} · ${new Date(t.createdAt).toLocaleDateString()}）</option>`).join('')}
      </select>
      <div id="templateValidationMsg" style="margin-top:8px;display:none;"></div>
    </div>`;

  document.getElementById('templateSelect').addEventListener('change', async (e) => {
    const tid = e.target.value;
    if (!tid || tid === '__chat__') {
      selectedTemplateId = null;
      window._selectedTemplateId = null;
      document.getElementById('templateValidationMsg').style.display = 'block';
      document.getElementById('templateValidationMsg').style.color = '#1677ff';
      document.getElementById('templateValidationMsg').textContent = '已选择对话模式，将通过聊天确认分析需求';
      window.dispatchEvent(new CustomEvent('template-selected', { detail: { templateId: null } }));
      return;
    }
    await handleTemplateSelect(tid);
  });
}

export async function handleTemplateSelect(templateId) {
  selectedTemplateId = templateId;
  window._selectedTemplateId = templateId;
  await validateTemplate(templateId);
}

export async function validateTemplate(templateId) {
  const batchId = getCurrentBatchId();
  const res = await post(`/templates/${templateId}/validate`, { batchId });
  const data = await res.json();

  const msgEl = document.getElementById('templateValidationMsg');
  if (data.valid) {
    msgEl.style.display = 'block';
    msgEl.style.color = '#52c41a';
    msgEl.textContent = '✓ 模版校验通过，可直接生成报告';
    window.dispatchEvent(new CustomEvent('template-selected', { detail: { templateId, valid: true } }));
  } else {
    msgEl.style.display = 'block';
    msgEl.style.color = '#ff4d4f';
    let msg = '';
    if (data.missingColumns.length > 0) msg += `缺少列: ${data.missingColumns.join(', ')} `;
    if (data.strategyConflict) msg += `策略冲突: 模版为${data.templateStrategy}，当前为${data.currentStrategy}`;
    msgEl.textContent = '✗ ' + msg + '。请选择其他模版或使用对话模式。';
  }
}

// ===== 模版管理页面 (T052a/T052b) =====
export async function loadTemplateList() {
  const res = await get('/templates');
  return (await res.json()).templates;
}

export async function deleteTemplate(id) {
  await del(`/templates/${id}`);
}

export async function viewTemplateDetail(id) {
  // 模版不可编辑 — 展示详情即可（维度/指标/图表/列名）
  const templates = await loadTemplateList();
  const t = templates.find(x => x.id === id);
  if (t) alert(`模版: ${t.name}\n策略: ${t.strategy}\n关联列: ${t.columnNames.join(', ')}\n保存时间: ${new Date(t.createdAt).toLocaleString()}`);
}
