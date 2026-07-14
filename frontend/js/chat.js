// AIExport 聊天模块
import { get, post } from './api.js';
import { getCurrentBatchId, getCurrentStrategy } from './upload.js';
import { getSelectedTemplateId } from './template.js';

let currentSessionId = null;
window._currentSessionId = null;

export function getCurrentSessionId() { return currentSessionId; }

export async function startChat(batchId, strategy) {
  const templateId = getSelectedTemplateId();
  const res = await post('/chat/start', { batchId, strategy, templateId });
  const data = await res.json();
  currentSessionId = data.sessionId;
  window._currentSessionId = currentSessionId;

  if (data.mode === 'template') {
    const cp = document.getElementById('centerPanel');
    if (cp) cp.style.display = 'none';
    const cb = document.getElementById('confirmBtn');
    if (cb) cb.style.display = 'none';
    const gb = document.getElementById('generateBtn');
    if (gb) gb.style.display = 'block';
  } else {
    const cp = document.getElementById('centerPanel');
    if (cp) cp.style.display = 'block';
    const cb = document.getElementById('confirmBtn');
    if (cb) cb.style.display = 'inline-block';
    const gb = document.getElementById('generateBtn');
    if (gb) gb.style.display = 'none';
    renderMessage({ sender: 'system', content: data.firstMessage });
  }
  return data;
}

let abortController = null;
export function isThinking() { return abortController !== null; }
export function stopThinking() {
  if (abortController) { abortController.abort(); abortController = null; }
  updateThinkingUI(false);
}

function updateThinkingUI(thinking) {
  const input = document.getElementById('chatInput');
  const confirmBtn = document.getElementById('confirmBtn');
  if (input) { input.disabled = thinking; input.placeholder = thinking ? ' 模型思考中，请稍候...' : '输入分析需求...'; }
  if (confirmBtn) {
    confirmBtn.textContent = thinking ? '停止思考' : '确认需求';
    confirmBtn.style.background = thinking ? '#ff4d4f' : '#1677ff';
    confirmBtn.onclick = thinking ? stopThinking : (() => window._handleConfirm?.());
  }
}

export async function sendMessage(content) {
  if (abortController) return; // 正在思考中，忽略重复发送
  const token = localStorage.getItem('token');
  abortController = new AbortController();
  updateThinkingUI(true);

  const msgId = 'stream-' + Date.now();
  const container = document.getElementById('chatMessages');
  const streamDiv = document.createElement('div');
  streamDiv.id = msgId;
  streamDiv.style.cssText = 'margin-bottom:12px;display:flex;justify-content:flex-start;';
  streamDiv.innerHTML = '<div style="max-width:85%;padding:10px 14px;border-radius:8px;font-size:14px;background:#f0f0f0;color:#333;"><div class="reasoning-area" style="display:none;margin-bottom:8px;font-size:12px;color:#888;"><details open><summary style="cursor:pointer;"> 思考中...</summary><div class="reasoning-content" style="margin-top:4px;padding:8px;background:#fffbe6;border-radius:4px;border-left:3px solid #faad14;white-space:pre-wrap;"></div></details></div><span class="stream-text"></span></div>';
  container.appendChild(streamDiv);
  scrollToBottom();

  let fullContent = '';
  let reasoning = '';

  try {
    const model = document.getElementById('modelSelect')?.value || null;
    const res = await fetch('http://localhost:5000/api/chat/message/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ sessionId: currentSessionId, content, model }),
      signal: abortController.signal
    });

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    let currentEvent = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';

      for (const line of lines) {
        if (line.startsWith('event: ')) { currentEvent = line.slice(7).trim(); continue; }
        if (!line.startsWith('data: ')) continue;

        const div = document.getElementById(msgId);
        if (!div) continue;
        const data = JSON.parse(line.slice(6));

        if (currentEvent === 'reasoning') {
          const r = data.text || '';
          if (r) {
            const reasonArea = div.querySelector('.reasoning-area');
            const reasonContent = div.querySelector('.reasoning-content');
            if (reasonArea) reasonArea.style.display = 'block';
            if (reasonContent) reasonContent.textContent += r;
          }
        } else if (currentEvent === 'token' || (data.text && !data.text.startsWith?.('__REASONING__:'))) {
          if (!data.text.startsWith?.('<!--reasoning:')) {
            fullContent += (data.text || '');
            const textEl = div.querySelector('.stream-text');
            if (textEl) textEl.innerHTML = renderMarkdown(fullContent);
          }
        }

        if (currentEvent === 'done') {
          const reasonSummary = div.querySelector('.reasoning-area summary');
          if (reasonSummary) reasonSummary.textContent = ' 思考过程';
        }
        if (data.reasoning) {
          const reasonArea = div.querySelector('.reasoning-area');
          if (reasonArea) reasonArea.style.display = 'block';
        }
      }
      scrollToBottom();
    }
    // 流正常结束
    abortController = null;
    updateThinkingUI(false);
  } catch (e) {
    if (e.name === 'AbortError') {
      document.getElementById(msgId).querySelector('.stream-text').innerHTML = renderMarkdown(fullContent) + '\n\n[已停止思考]';
    } else {
      document.getElementById(msgId).querySelector('.stream-text').innerHTML = '请求失败: ' + e.message;
    }
  } finally {
    abortController = null;
    updateThinkingUI(false);
  }

  if (fullContent.includes('正在生成报告')) {
    const m = fullContent.match(/任务ID: ([a-f0-9-]+)/i);
    if (m) window.dispatchEvent(new CustomEvent('report-confirmed', { detail: { taskId: m[1] } }));
  }
}

function renderMarkdown(text) {
  if (!text) return '';
  let html = text
    // 转义 HTML
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    // 粗体 **text** 或 __text__
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/__(.+?)__/g, '<strong>$1</strong>')
    // 行内代码 `code`
    .replace(/`([^`]+)`/g, '<code style="background:#ffe8e8;padding:2px 4px;border-radius:3px;font-size:13px;">$1</code>')
    // 标题 ### / ## / #
    .replace(/^### (.+)$/gm, '<h4 style="margin:4px 0;">$1</h4>')
    .replace(/^## (.+)$/gm, '<h3 style="margin:4px 0;">$1</h3>')
    .replace(/^# (.+)$/gm, '<h2 style="margin:4px 0;">$1</h2>')
    // 无序列表 - item 或 * item
    .replace(/^[\-\*] (.+)$/gm, '<li>$1</li>')
    // 数字列表 1. item
    .replace(/^\d+\. (.+)$/gm, '<li>$1</li>')
    // 换行
    .replace(/\n\n/g, '<br><br>')
    .replace(/\n/g, '<br>');
  // 包裹连续的 <li>
  html = html.replace(/(<li>.*?<\/li>(?:<br>)?)+/g, '<ul style="padding-left:20px;margin:4px 0;">$&</ul>');
  return html;
}

export function renderMessage(msg) {
  const container = document.getElementById('chatMessages');
  if (!container) return;
  const isUser = msg.sender === 'user';
  const contentHtml = renderMarkdown(msg.content);
  const reasoningHtml = msg.reasoning
    ? `<details style="margin-top:6px;font-size:12px;color:#888;"><summary style="cursor:pointer;color:#1677ff;"> 思考过程</summary><div style="margin-top:4px;padding:8px;background:#fffbe6;border-radius:4px;border-left:3px solid #faad14;white-space:pre-wrap;">${msg.reasoning}</div></details>`
    : '';

  container.innerHTML += `
    <div style="margin-bottom:12px;display:flex;justify-content:${isUser ? 'flex-end' : 'flex-start'};">
      <div style="max-width:${isUser ? '70%' : '85%'};padding:10px 14px;border-radius:8px;font-size:14px;line-height:1.6;
        ${isUser ? 'background:#1677ff;color:#fff;' : 'background:#f0f0f0;color:#333;'}">
        ${contentHtml}
        ${reasoningHtml}
      </div>
    </div>`;
}

export function scrollToBottom() {
  const container = document.getElementById('chatMessages');
  if (container) container.scrollTop = container.scrollHeight;
}

export async function handleConfirm() {
  const messageInput = document.getElementById('chatInput');
  const content = messageInput?.value.trim();
  if (content) {
    renderMessage({ sender: 'user', content });
    await sendMessage(content);
    messageInput.value = '';
  } else {
    const res = await post('/chat/confirm', { sessionId: currentSessionId });
    const data = await res.json();
    window.dispatchEvent(new CustomEvent('report-confirmed', { detail: { taskId: data.taskId } }));
  }
}

export function handleTimeout() {
  document.getElementById('sessionTimeoutMsg').style.display = 'block';
  document.getElementById('sessionTimeoutMsg').textContent = '会话已超时（30分钟），请刷新页面重新上传。';
}
window.addEventListener('session-timeout', handleTimeout);
