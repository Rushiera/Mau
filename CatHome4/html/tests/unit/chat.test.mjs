// tests/unit/chat.test.mjs —— 对话区核心逻辑单元测试（chat.js 全局函数）
// 覆盖：阶段模型五态渲染（reasoning/text/toolCalls/done/error）+ 工具回填 + 终态 + 用户消息 + 阶段封口
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

async function runGlobalScript(url) {
  const code = await readFile(url, 'utf-8');
  vm.runInThisContext(code, { filename: url.toString() });
}

beforeAll(async () => {
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/chat.js', import.meta.url));
});

// 每个测试前重置对话状态（sending 态 + 清空气泡 + 清理定时器）
beforeEach(() => {
  window.chatState = 'sending';
  window.chatStage = null;
  window.chatToolQueue = [];
  if (window.chatTimer) { clearTimeout(window.chatTimer); window.chatTimer = null; }
  document.getElementById('chatMsgs').textContent = '';
});

function msgs() {
  return document.getElementById('chatMsgs').querySelectorAll('.chat-row');
}

// ── 阶段模型：reasoning ──
test('reasoning 事件创建思考气泡并流式追加', () => {
  window.chatOnLlm({ kind: 'reasoning', text: '第一步' });
  expect(msgs().length).toBe(1);
  expect(msgs()[0].querySelector('.chat-bubble.reason')).not.toBeNull();
  expect(window.chatStage.type).toBe('reason');

  window.chatOnLlm({ kind: 'reasoning', text: '第二步' });
  expect(window.chatStage.reasonPre.textContent).toBe('第一步第二步');
});

// ── 阶段模型：text ──
test('text 事件创建回复气泡并流式追加', () => {
  window.chatOnLlm({ kind: 'text', text: '你好' });
  expect(msgs().length).toBe(1);
  expect(msgs()[0].querySelector('.chat-bubble')).not.toBeNull();
  expect(window.chatStage.type).toBe('text');

  window.chatOnLlm({ kind: 'text', text: '世界' });
  expect(msgs()[0].querySelector('.chat-bubble').textContent).toBe('你好世界');
});

test('text 空增量跳过——不建空气泡', () => {
  window.chatOnLlm({ kind: 'text', text: '' });
  expect(msgs().length).toBe(0);
  expect(window.chatStage).toBeNull();
});

// ── 阶段模型：toolCalls ──
test('toolCalls 事件创建工具气泡并入队', () => {
  window.chatOnLlm({
    kind: 'toolCalls',
    text: JSON.stringify([{ id: 'c1', type: 'function', function: { name: 'time', arguments: '{}' } }])
  });
  expect(window.chatStage.type).toBe('tool');
  expect(window.chatToolQueue.length).toBe(1);
  expect(window.chatToolQueue[0].name).toBe('time');
  // 工具气泡存在
  const toolCards = document.getElementById('chatMsgs').querySelectorAll('.chat-tool');
  expect(toolCards.length).toBe(1);
});

// ── 阶段模型：done ──
test('done 事件停光标（streaming → streaming-wait）', () => {
  window.chatOnLlm({ kind: 'text', text: '内容' });
  const bubble = window.chatStage.bubble;
  expect(bubble.classList.contains('streaming')).toBe(true);

  window.chatOnLlm({ kind: 'done' });
  expect(bubble.classList.contains('streaming')).toBe(false);
  expect(bubble.classList.contains('streaming-wait')).toBe(true);
});

// ── 阶段模型：error ──
test('error 事件追加错误文本并恢复 idle', () => {
  window.chatOnLlm({ kind: 'text', text: '部分内容' });
  const bubble = window.chatStage.bubble;   // 保存引用（error 分支末尾置 chatStage=null）
  window.chatOnLlm({ kind: 'error', text: 'LLM 挂了' });
  expect(bubble.textContent).toContain('LLM 挂了');
  expect(bubble.classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
  expect(window.chatStage).toBeNull();
});

// ── 工具结果回填 ──
test('tool 事件按 FIFO 回填工具卡', () => {
  window.chatOnLlm({
    kind: 'toolCalls',
    text: JSON.stringify([
      { id: 'c1', function: { name: 'time', arguments: '{}' } },
      { id: 'c2', function: { name: 'random', arguments: '{}' } }
    ])
  });
  expect(window.chatToolQueue.length).toBe(2);

  window.chatOnTool({ name: 'time', result: '2026-08-27 10:00:00' });
  expect(window.chatToolQueue[0].filled).toBe(true);
  expect(window.chatToolQueue[0].card.querySelector('.tr').textContent).toBe('2026-08-27 10:00:00');

  window.chatOnTool({ name: 'random', result: '42' });
  expect(window.chatToolQueue[1].filled).toBe(true);
});

// ── 终态 chatdone ──
test('chatdone 终态：seal 阶段 + 未回填工具卡标注 + 恢复 idle', () => {
  window.chatOnLlm({
    kind: 'toolCalls',
    text: JSON.stringify([{ id: 'c1', function: { name: 'time', arguments: '{}' } }])
  });
  // 未回填工具卡
  window.chatOnChatDone({});
  expect(window.chatToolQueue.length).toBe(0);
  expect(window.chatState).toBe('idle');
  // 未回填标注
  const wu = document.getElementById('chatMsgs').querySelectorAll('.ta');
  expect(wu.length).toBeGreaterThan(0);
});

// ── 用户消息 ──
test('user 事件渲染用户气泡（含插话队列 FIFO 移除）', () => {
  window.chatPending = ['测试消息'];
  window.chatRenderPending();
  window.chatOnUser({ content: '测试消息', source: 'user' });
  const rows = msgs();
  expect(rows[0].classList.contains('user')).toBe(true);
  expect(rows[0].querySelector('.chat-bubble').textContent).toBe('测试消息');
  // 插话队列已移除
  expect(window.chatPending.length).toBe(0);
});

test('system 源消息带 [SystemAuto] 前缀', () => {
  window.chatOnUser({ content: '自动拉起', source: 'system' });
  const bubble = msgs()[0].querySelector('.chat-bubble');
  expect(bubble.textContent).toBe('[SystemAuto] 自动拉起');
  expect(msgs()[0].querySelector('.chat-bubble.system')).not.toBeNull();
});

// ── 阶段封口 ──
test('chatSealCurrent 移除空 text 气泡（无文本无子元素）', () => {
  window.chatNewStage('text');
  expect(msgs().length).toBe(1);
  window.chatSealCurrent();
  expect(msgs().length).toBe(0);
  expect(window.chatStage).toBeNull();
});

test('chatSealCurrent 保留有内容的 reason 气泡（details 子元素存在不移除）', () => {
  window.chatOnLlm({ kind: 'reasoning', text: '有思考内容' });
  expect(msgs().length).toBe(1);
  window.chatSealCurrent();
  expect(msgs().length).toBe(1);
  expect(window.chatStage).toBeNull();
});
