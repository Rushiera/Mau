// tests/unit/chat-view.test.mjs —— chat.html 独立对话页 view 协议渲染单元测试（F1）
// 覆盖：view 事件六种 renderType 分发（user/stream/text/reason/toolcard/control）+ 流式容器复用 + 整块替换 + history blocks 渲染
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

// 单独构造 chat.html 的 JSDOM（setup.js 加载的是 index.html——本测试文件覆盖为 chat.html）
let chatDom;
let chatMsgs;
let chatStatus;

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  chatDom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  // 覆盖全局 document/window（本测试域专用——chat.html 内联脚本）
  globalThis.window = globalThis;
  globalThis.document = chatDom.window.document;
  globalThis.Node = chatDom.window.Node;
  globalThis.HTMLElement = chatDom.window.HTMLElement;
  // chat.html 脚本执行
  const m = html.match(/<script>([\s\S]*?)<\/script>/);
  vm.runInThisContext(m[1], { filename: 'chat.html' });
  chatMsgs = document.getElementById('chatMsgs');
  chatStatus = document.getElementById('chatStatus');
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
  expect(window.chatInfo.textContent).toContain('s1');
});
