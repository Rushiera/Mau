// tests/unit/chat-scroll.test.mjs —— 对话区滚动自持层单元测试（design-ch4-frontend-scroll）
// 覆盖：滚轮增量归一 · 兜底转发判据（空白 / 豁免面 / 对话区内 / Ctrl）· 导航项收集判据 · 带体几何纯函数 · 刻度重建
// 加载方式：setup.js 已装配 chat.html 骨架到全局 DOM（与生产同构）；本文件只加载被测模块 js/chat-scroll.js
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

let chatMsgs;

beforeAll(async () => {
  // 观察器补齐——setup.js 只把 document/window/Node/Event/HTMLElement 挂全局；模块加载期需 MutationObserver 在场
  globalThis.MutationObserver = document.defaultView.MutationObserver;
  const src = await readFile(new URL('../../js/chat-scroll.js', import.meta.url), 'utf-8');
  vm.runInThisContext(src, { filename: 'chat-scroll.js' });
  chatMsgs = document.getElementById('chatMsgs');
});

beforeEach(() => {
  chatMsgs.textContent = '';
  globalThis.chatBandItems = [];
  globalThis.chatBandDragging = false;
});

// 行构造——与 chat-view.js chatBubble 同构（.chat-row.user / .chat-row.assistant > .chat-bubble[.cls]）
function mkRow(role, cls) {
  const row = document.createElement('div');
  row.className = 'chat-row ' + role;
  const b = document.createElement('div');
  b.className = 'chat-bubble' + (cls ? ' ' + cls : '');
  row.appendChild(b);
  chatMsgs.appendChild(row);
  return row;
}

// 滚轮事件桩——只需 target / deltaY / deltaMode / ctrlKey / preventDefault
function mkWheel(target, deltaY, deltaMode, ctrlKey) {
  const ev = {
    target: target,
    deltaY: deltaY,
    deltaMode: deltaMode === undefined ? 0 : deltaMode,
    ctrlKey: ctrlKey === true,
    prevented: 0,
    preventDefault: function () { ev.prevented = ev.prevented + 1; }
  };
  return ev;
}

// scrollTop 探针——jsdom 无布局（scrollTop 恒被夹取为 0），用实例属性覆盖取值与写入
function withScrollSpy(fn) {
  let last = null;
  Object.defineProperty(chatMsgs, 'scrollTop', {
    get: function () { return 500; },
    set: function (v) { last = v; },
    configurable: true
  });
  try {
    fn();
  } finally {
    delete chatMsgs.scrollTop;
  }
  return last;
}

// ── 滚轮增量归一 ──
test('滚轮增量归一——像素 / 行 / 页三态', () => {
  expect(chatWheelDelta(120, 0, 600)).toBe(120);
  expect(chatWheelDelta(3, 1, 600)).toBe(48);
  expect(chatWheelDelta(1, 2, 600)).toBe(600);
  expect(chatWheelDelta(1, 2, 0)).toBe(400);
  expect(chatWheelDelta(0, 0, 600)).toBe(0);
});

// ── 兜底转发判据 ──
test('兜底转发——空白区（body）接管并驱动对话区', () => {
  const ev = mkWheel(document.body, 100, 0);
  const top = withScrollSpy(() => { chatWheelForward(ev); });
  expect(ev.prevented).toBe(1);
  expect(top).toBe(600);
});

test('兜底转发——豁免面（textarea）不接管', () => {
  const ta = document.getElementById('chatSendInput');
  const ev = mkWheel(ta, 100, 0);
  chatWheelForward(ev);
  expect(ev.prevented).toBe(0);
});

test('兜底转发——对话区内不接管（走原生滚动）', () => {
  mkRow('user');
  const bubble = chatMsgs.querySelector('.chat-bubble');
  const ev = mkWheel(bubble, 100, 0);
  chatWheelForward(ev);
  expect(ev.prevented).toBe(0);
});

test('兜底转发——Ctrl+滚轮不接管（浏览器缩放）', () => {
  const ev = mkWheel(document.body, 100, 0, true);
  chatWheelForward(ev);
  expect(ev.prevented).toBe(0);
});

test('兜底转发——标题栏 / 状态条等非滚动面同样接管', () => {
  const bar = document.getElementById('chatbar');
  const ev = mkWheel(bar, 60, 0);
  chatWheelForward(ev);
  expect(ev.prevented).toBe(1);
});

// ── 导航项收集判据 ──
test('导航项收集——只收 user / 正文 / 结算三类，其余排除', () => {
  mkRow('user');
  mkRow('assistant');
  mkRow('assistant', 'tool');
  mkRow('assistant', 'reason');
  mkRow('assistant', 'inject');
  mkRow('assistant', 'error');
  mkRow('assistant', 'retry');
  mkRow('assistant', 'stall');
  mkRow('assistant', 'paused');
  const items = chatNavCollect();
  expect(items.length).toBe(2);
  expect(items[0].role).toBe('user');
  expect(items[1].role).toBe('reply');
});

test('导航项收集——本轮结算块独立成类（sum）', () => {
  mkRow('assistant', 'roundsum');
  const items = chatNavCollect();
  expect(items.length).toBe(1);
  expect(items[0].role).toBe('sum');
});

test('导航项收集——工具卡两态入带（展开 tool / 折叠 tool-fold）', () => {
  const row = mkRow('assistant', 'tool');
  const det = document.createElement('details');
  det.className = 'chat-tool';
  row.querySelector('.chat-bubble').appendChild(det);
  det.open = false;
  let items = chatNavCollect();
  expect(items.length).toBe(1);
  expect(items[0].role).toBe('tool-fold');
  det.open = true;
  items = chatNavCollect();
  expect(items.length).toBe(1);
  expect(items[0].role).toBe('tool');
});

test('导航项收集——run 态工具卡不入带（即使展开）', () => {
  const row = mkRow('assistant', 'tool pending');
  const det = document.createElement('details');
  det.className = 'chat-tool';
  det.open = true;
  row.querySelector('.chat-bubble').appendChild(det);
  expect(chatNavCollect().length).toBe(0);
});

test('导航项收集——思考块两态入带（展开 think / 压缩 think-fold / 流式不入）', () => {
  const row = mkRow('assistant', 'reason');
  const box = document.createElement('div');
  box.className = 'chat-think done';
  row.querySelector('.chat-bubble').appendChild(box);
  let items = chatNavCollect();
  expect(items.length).toBe(1);
  expect(items[0].role).toBe('think-fold');
  box.classList.add('full');
  items = chatNavCollect();
  expect(items.length).toBe(1);
  expect(items[0].role).toBe('think');
  box.classList.remove('done');
  box.classList.add('stream');
  expect(chatNavCollect().length).toBe(0);
});

test('导航项收集——无气泡行跳过（防结构漂移）', () => {
  const row = document.createElement('div');
  row.className = 'chat-row assistant';
  chatMsgs.appendChild(row);
  expect(chatNavCollect().length).toBe(0);
});

// ── 刻度重建 ──
test('刻度重建——每条导航消息一道刻度，角色类名区分', () => {
  mkRow('user');
  mkRow('assistant');
  mkRow('assistant', 'tool');
  chatBandBuildTicks();
  const ticks = document.querySelectorAll('#sbTicks .sb-tick');
  expect(ticks.length).toBe(2);
  expect(ticks[0].classList.contains('user')).toBe(true);
  expect(ticks[1].classList.contains('reply')).toBe(true);
  expect(ticks[0].getAttribute('data-idx')).toBe('0');
});

test('刻度重建——子节点变化经观察器自动重建', async () => {
  mkRow('user');
  await new Promise((resolve) => { setTimeout(resolve, 0); });
  expect(document.querySelectorAll('#sbTicks .sb-tick').length).toBe(1);
});

// ── 带体几何（纯函数） ──
test('指示块几何——高度 = 视口占比，位置按滚动比例', () => {
  const top = chatBandThumb(0, 4000, 1000, 400);
  expect(top.height).toBe(100);
  expect(top.top).toBe(0);
  const mid = chatBandThumb(1500, 4000, 1000, 400);
  expect(mid.top).toBe(150);
  const bottom = chatBandThumb(3000, 4000, 1000, 400);
  expect(bottom.top).toBe(300);
});

test('指示块几何——最小高与边界夹取', () => {
  expect(chatBandThumb(0, 100000, 1000, 400).height).toBe(24);
  expect(chatBandThumb(999999, 4000, 1000, 400).top).toBe(300);
  expect(chatBandThumb(-50, 4000, 1000, 400).top).toBe(0);
  expect(chatBandThumb(0, 0, 0, 0)).toEqual({ top: 0, height: 0 });
});

test('刻度几何——高度按块真实像素高比例映射', () => {
  const g = chatBandTickGeom(0, 400, 2000, 4000, 400);
  expect(g.top).toBe(0);
  expect(g.height).toBe(40);
});

test('刻度几何——最小高 / 不越过下一条 / 末条用带体底 / 不封顶', () => {
  expect(chatBandTickGeom(0, 1, 2000, 4000, 400).height).toBe(2);
  expect(chatBandTickGeom(0, 400, 100, 4000, 400).height).toBe(9);
  expect(chatBandTickGeom(0, 400, -1, 4000, 400).height).toBe(40);
  expect(chatBandTickGeom(0, 4000, -1, 4000, 400).height).toBe(400);
  expect(chatBandTickGeom(0, 400, 100, 0, 0)).toEqual({ top: 0, height: 0 });
});

test('刻度坐标与比例换算——含上下夹取', () => {
  expect(chatBandTickY(2000, 4000, 400)).toBe(200);
  expect(chatBandTickY(-100, 4000, 400)).toBe(0);
  expect(chatBandTickY(999999, 4000, 400)).toBe(398);
  expect(chatBandRatioToScroll(0.5, 4000, 1000)).toBe(1500);
  expect(chatBandRatioToScroll(2, 4000, 1000)).toBe(3000);
  expect(chatBandRatioToScroll(-1, 4000, 1000)).toBe(0);
  expect(chatBandRatioToScroll(0.5, 1000, 1000)).toBe(0);
});
