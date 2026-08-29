// tests/unit/chat-view.test.mjs —— chat.html 独立对话页 view 协议渲染单元测试（F1 原 11 用例 + F2.4 门禁迁移并入旧 chat.test.mjs 用例）
// 覆盖：view 事件六种 renderType 分发（user/stream/text/reason/toolcard/control）+ 流式容器复用 + 整块替换 + history blocks 渲染
//       + 发送流程（chatSend→link 相位）+ E 系列四态相位 + Token 统计 + Note 面板（F2.4 补 chat.html 单测盲区）
// 加载方式（F2.4 重构）：不再正则提取内联 <script>——chat.html 引导层已外置核心至 js/chat-core.js + js/chat-note.js（对话逻辑唯一真相源）
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

let chatMsgs;
let chatStatus;
let chatInfo;

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  const chatDom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  // 覆盖全局 document/window（本测试域专用——chat.html 引导层）
  globalThis.window = globalThis;
  globalThis.document = chatDom.window.document;
  globalThis.Node = chatDom.window.Node;
  globalThis.HTMLElement = chatDom.window.HTMLElement;
  // F2.4 门禁迁移——加载外部模块（对话核心 + Note 面板；不执行引导层 SSE/按钮绑定/初始化）
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const note = await readFile(new URL('../../js/chat-note.js', import.meta.url), 'utf-8');
  vm.runInThisContext(core, { filename: 'chat-core.js' });
  vm.runInThisContext(note, { filename: 'chat-note.js' });
  chatMsgs = document.getElementById('chatMsgs');
  chatStatus = document.getElementById('chatStatus');
  chatInfo = document.getElementById('chatInfo');
});

// 每个测试前重置状态
beforeEach(() => {
  window.chatState = 'sending';
  window.viewContainers = {};
  window.chatPending = [];
  if (window.chatTimer) { clearTimeout(window.chatTimer); window.chatTimer = null; }
  chatMsgs.textContent = '';
  chatStatus.textContent = '';
  window.chatPhaseReset();
});

function rows() {
  return chatMsgs.querySelectorAll('.chat-row');
}

function bubbles() {
  return chatMsgs.querySelectorAll('.chat-bubble');
}

// ── user 视图块 ──
test('view user 渲染用户气泡（含 system 前缀）', () => {
  window.chatOnView({ seq: 1, renderType: 'user', payload: { content: '你好', source: 'user' }, replaceSeq: -1 });
  expect(rows().length).toBe(1);
  expect(rows()[0].classList.contains('user')).toBe(true);
  expect(bubbles()[0].textContent).toBe('你好');

  window.chatOnView({ seq: 2, renderType: 'user', payload: { content: '自动拉起', source: 'system' }, replaceSeq: -1 });
  expect(rows().length).toBe(2);
  expect(bubbles()[1].textContent).toBe('[SystemAuto] 自动拉起');
  expect(bubbles()[1].classList.contains('system')).toBe(true);
});

// ── stream 流式容器（text）──
test('view stream text 创建回复容器并流式追加（同 seq 复用）', () => {
  // 新 seq → 新建容器
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '你' }, replaceSeq: -1 });
  expect(rows().length).toBe(1);
  expect(window.viewContainers[10].type).toBe('text');
  // 同 seq → 追加到同一容器
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '好' }, replaceSeq: -1 });
  expect(bubbles()[0].textContent).toBe('你好');
  // 容器未删除——仍可继续追加
  expect(window.viewContainers[10]).not.toBeUndefined();
});

// ── stream 流式容器（reasoning）──
test('view stream reasoning 创建思考容器并流式追加', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  expect(window.viewContainers[11].type).toBe('reason');
  const pre = window.viewContainers[11].reasonPre;
  expect(pre.textContent).toBe('思考');
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '中' }, replaceSeq: -1 });
  expect(pre.textContent).toBe('思考中');
});

// ── text 整块替换流式容器 ──
test('view text 整块替换对应流式容器（replaceSeq 命中）', () => {
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '流式' }, replaceSeq: -1 });
  const bubbleBefore = bubbles()[0];
  // 整块替换——replaceSeq=10 命中流式容器 → 内容替换为整块 + 容器删除
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: '最终整块' }, replaceSeq: 10 });
  expect(bubbles().length).toBe(1);
  expect(bubbles()[0]).toBe(bubbleBefore);   // 同一 DOM 节点
  expect(bubbles()[0].textContent).toBe('最终整块');
  expect(window.viewContainers[10]).toBeUndefined();
});

// ── text 整块无容器（无流式直接整块）──
test('view text 无命中容器时新建气泡', () => {
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: '直接整块' }, replaceSeq: -1 });
  expect(rows().length).toBe(1);
  expect(bubbles()[0].textContent).toBe('直接整块');
});

// ── reason 整块 ──
test('view reason 整块替换思考流式容器', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '最终思考' }, replaceSeq: 11 });
  expect(window.viewContainers[11]).toBeUndefined();
  const reasonDetail = chatMsgs.querySelector('.chat-reason div');
  expect(reasonDetail.textContent).toBe('最终思考');
});

// ── toolcard 整块 ──
test('view toolcard 渲染工具卡（含名称/参数/结果）', () => {
  window.chatOnView({
    seq: 30, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-08-30 00:00:00' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card).not.toBeNull();
  expect(card.querySelector('.tn').textContent).toContain('time');
  expect(card.querySelector('.tr').textContent).toBe('2026-08-30 00:00:00');
  // 工具态
  expect(window.chatActivePhase).toBe('tool');
});

// ── control usage ──
test('view control usage 渲染 Token 统计', () => {
  window.chatOnView({ seq: 40, renderType: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } }, replaceSeq: -1 });
  expect(chatStatus.textContent).toContain('↑100');
  expect(chatStatus.textContent).toContain('↓20');
  expect(chatStatus.textContent).toContain('cache 30');
});

// ── control error ──
test('view control error 错误提示 + 恢复 idle', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'text', text: '部分' }, replaceSeq: -1 });
  window.chatOnView({ seq: 41, renderType: 'control', payload: { type: 'error', text: 'LLM 挂了' }, replaceSeq: -1 });
  expect(bubbles()[0].textContent).toContain('LLM 挂了');
  expect(bubbles()[0].classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
  expect(Object.keys(window.viewContainers).length).toBe(0);
});

// ── control chatdone 终态 ──
test('view control chatdone seal 流式容器 + 恢复 idle', () => {
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '流式' }, replaceSeq: -1 });
  const bubble = bubbles()[0];
  expect(bubble.classList.contains('streaming')).toBe(true);
  window.chatOnView({ seq: 42, renderType: 'control', payload: { type: 'chatdone', count: 3 }, replaceSeq: -1 });
  expect(bubble.classList.contains('streaming')).toBe(false);
  expect(window.chatState).toBe('idle');
  expect(Object.keys(window.viewContainers).length).toBe(0);
  expect(chatInfo.textContent).toContain('3 条');
});

// ── history blocks 渲染 ──
test('chatRenderHistory 渲染 view blocks 结构', () => {
  window.chatRenderHistory({
    version: 1,
    sessionId: 's1',
    count: 3,
    blocks: [
      { seq: 1, id: '1:h1', renderType: 'user', payload: { content: '用户' } },
      { seq: 2, id: '2:h2', renderType: 'reason', payload: { content: '思考' } },
      { seq: 3, id: '3:h3', renderType: 'text', payload: { content: '回复' } },
      { seq: 4, id: '4:h4', renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' } }
    ]
  });
  expect(rows().length).toBe(4);
  expect(bubbles()[0].textContent).toBe('用户');
  expect(chatMsgs.querySelector('.chat-reason div').textContent).toBe('思考');
  expect(bubbles()[2].textContent).toBe('回复');
  expect(chatMsgs.querySelector('.chat-tool')).not.toBeNull();
  expect(chatInfo.textContent).toContain('s1');
});

// ── E 系列：四态相位切换（F2.4 自旧 chat.test.mjs 迁移至 view 协议）──
test('E 四态相位切换——link/think/tool/reply 事件驱动', () => {
  // link：idle 发送进入链路态（chatSend 走 command 总线 + 本地插话队列）
  window.chatState = 'idle';
  window.chatInput.value = '你好';
  window.chatSend();
  expect(window.chatActivePhase).toBe('link');
  expect(window.chatPending.length).toBe(1);
  expect(window.chatState).toBe('sending');
  // think：首 reasoning
  window.chatOnView({ seq: 1, renderType: 'stream', payload: { kind: 'reasoning', text: '思考中' }, replaceSeq: -1 });
  expect(window.chatActivePhase).toBe('think');
  // tool：toolcard
  window.chatOnView({ seq: 2, renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' }, replaceSeq: -1 });
  expect(window.chatActivePhase).toBe('tool');
  // reply：首 text
  window.chatOnView({ seq: 3, renderType: 'stream', payload: { kind: 'text', text: '回复' }, replaceSeq: -1 });
  expect(window.chatActivePhase).toBe('reply');
});

// ── E 系列：Token 统计（F2.4 迁移）──
test('E usage 事件渲染 Token 统计（含 miss）', () => {
  window.chatOnView({ seq: 40, renderType: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } }, replaceSeq: -1 });
  const bar = chatStatus;
  expect(bar.textContent).toContain('↑100');
  expect(bar.textContent).toContain('↓20');
  expect(bar.textContent).toContain('cache 30');
  expect(bar.textContent).toContain('miss 70');
});

// ── 发送流程：user 事件 FIFO 移除插话队列（F2.4 迁移）──
test('user 事件渲染用户气泡（含插话队列 FIFO 移除）', () => {
  window.chatPending = ['测试消息'];
  window.chatRenderPending();
  window.chatOnView({ seq: 5, renderType: 'user', payload: { content: '测试消息', source: 'user' }, replaceSeq: -1 });
  const userRows = rows();
  expect(userRows[0].classList.contains('user')).toBe(true);
  expect(userRows[0].querySelector('.chat-bubble').textContent).toBe('测试消息');
  expect(window.chatPending.length).toBe(0);
});

// ── Note 面板（F2.4 补盲区——chat-note.js）──
test('Note 面板：空态/渲染三态 + 展开收起', () => {
  // 空态
  window.noteState = { tasks: [], current: 0, done: 0 };
  window.noteRender();
  expect(document.getElementById('noteTitle').textContent).toBe('Note · 空');
  // 三态渲染
  window.noteState = { tasks: ['任务A', '任务B', '任务C'], current: 1, done: 1 };
  window.noteRender();
  expect(document.getElementById('noteTitle').textContent).toBe('Note (2/3)');
  const rows2 = document.querySelectorAll('#noteList .note-row');
  expect(rows2.length).toBe(3);
  expect(rows2[0].classList.contains('done')).toBe(true);
  expect(rows2[1].classList.contains('current')).toBe(true);
  // 展开收起
  window.noteExpanded = false;
  window.noteToggle();
  expect(document.getElementById('notePanel').classList.contains('collapsed')).toBe(false);
  window.noteToggle();
  expect(document.getElementById('notePanel').classList.contains('collapsed')).toBe(true);
});

test('Note 事件——计划存在自动展开面板', () => {
  window.noteExpanded = false;
  document.getElementById('notePanel').classList.add('collapsed');
  window.noteOnEvent({ state: { tasks: ['计划'], current: 0, done: 0 } });
  expect(window.noteState.tasks.length).toBe(1);
  expect(document.getElementById('notePanel').classList.contains('collapsed')).toBe(false);
});

// ── 失败流程（F2.4 迁移）──
test('chatFail 失败：seal 流式容器 + 独立错误气泡 + 恢复 idle', () => {
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '部分' }, replaceSeq: -1 });
  window.chatFail('发送失败');
  expect(window.chatState).toBe('idle');
  expect(Object.keys(window.viewContainers).length).toBe(0);
  const errBubbles = chatMsgs.querySelectorAll('.chat-bubble.error');
  expect(errBubbles.length).toBeGreaterThan(0);
  expect(errBubbles[errBubbles.length - 1].textContent).toBe('发送失败');
});
