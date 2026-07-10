// AIExport 聊天模块
import { get, post } from './api.js';
import { getCurrentBatchId, getCurrentStrategy } from './upload.js';
import { getSelectedTemplateId } from './template.js';

let currentSessionId = null;

export function getCurrentSessionId() { return currentSessionId; }

export async function startChat(batchId, strategy) {
  const templateId = getSelectedTemplateId();
  const res = await post('/chat/start', { batchId, strategy, templateId });
  const data = await res.json();
  currentSessionId = data.sessionId;

  if (data.mode === 'template') {
    // 模版模式：不需要聊天，显示"立即生成报告"按钮
    document.getElementById('chatArea').style.display = 'none';
    document.getElementById('confirmBtn').style.display = 'none';
    document.getElementById('generateBtn').style.display = 'inline-block';
  } else {
    // 对话模式
    document.getElementById('chatArea').style.display = 'block';
    document.getElementById('confirmBtn').style.display = 'inline-block';
    document.getElementById('generateBtn').style.display = 'none';
    renderMessage({ sender: 'system', content: data.firstMessage });
  }

  return data;
}

export async function sendMessage(content) {
  const res = await post('/chat/message', { sessionId: currentSessionId, content });
  const data = await res.json();
  renderMessage({ sender: 'system', content: data.content });
  scrollToBottom();

  // 检测确认回复 → 跳转报告等待页
  if (data.content?.includes('正在生成报告')) {
    const taskIdMatch = data.content.match(/任务ID: ([a-f0-9-]+)/i);
    if (taskIdMatch) {
      window.dispatchEvent(new CustomEvent('report-confirmed', { detail: { taskId: taskIdMatch[1] } }));
    }
  }
}

export function renderMessage(msg) {
  const container = document.getElementById('chatMessages');
  if (!container) return;
  const isUser = msg.sender === 'user';
  container.innerHTML += `
    <div style="margin-bottom:12px;display:flex;justify-content:${isUser ? 'flex-end' : 'flex-start'};">
      <div style="max-width:70%;padding:10px 14px;border-radius:8px;font-size:14px;line-height:1.6;
        ${isUser ? 'background:#1677ff;color:#fff;' : 'background:#f0f0f0;color:#333;'}">
        ${msg.content.replace(/\n/g, '<br>')}
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
    // 直接点击确认按钮
    const res = await post('/chat/confirm', { sessionId: currentSessionId });
    const data = await res.json();
    window.dispatchEvent(new CustomEvent('report-confirmed', { detail: { taskId: data.taskId } }));
  }
}

// ===== 会话超时处理 (T058a) =====
export function handleTimeout() {
  alert('会话已超时（30分钟），请重新上传文件开始。');
  window.location.hash = '#main';
  window.location.reload();
}

// 在 api.js 中拦截 410 状态码来触发超时通知
// 监听全局 410 事件
window.addEventListener('session-timeout', handleTimeout);
