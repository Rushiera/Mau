// tests/unit/chat-view.test.mjs —— chat.html 独立对话页 view 协议渲染单元测试（F1 原 11 用例 + F2.4 门禁迁移并入旧 chat.test.mjs 用例）
// 覆盖：view 事件六种 renderType 分发（user/stream/text/reason/toolcard/control）+ 流式容器复用 + 整块替换 + history blocks 渲染
//       + 发送流程（chatSend→link 相位）+ E 系列四态相位 + Token 统计 + Note 面板（F2.4 补 chat.html 单测盲区）
// 加载方式（F2.4 重构）：不再正则提取内联 <script>——chat.html 引导层已外置核心至 js/chat-core.js + js/chat-note.js（对话逻辑唯一真相源）
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach, vi } from 'vitest';

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
  // F2.4 门禁迁移——加载外部模块（公共工具 + MD 解析器 + 渲染面 + 对话核心 + Note 面板；不执行引导层 SSE/按钮绑定/初始化）
  const common = await readFile(new URL('../../js/ui-common.js', import.meta.url), 'utf-8');
  const md = await readFile(new URL('../../js/chat-md.js', import.meta.url), 'utf-8');
  const cmd = await readFile(new URL('../../js/chat-cmd.js', import.meta.url), 'utf-8');
  const tools = await readFile(new URL('../../js/chat-tools.js', import.meta.url), 'utf-8');
  const view = await readFile(new URL('../../js/chat-view.js', import.meta.url), 'utf-8');
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const note = await readFile(new URL('../../js/chat-note.js', import.meta.url), 'utf-8');
  vm.runInThisContext(common, { filename: 'ui-common.js' });
  vm.runInThisContext(md, { filename: 'chat-md.js' });
  vm.runInThisContext(cmd, { filename: 'chat-cmd.js' });
  vm.runInThisContext(tools, { filename: 'chat-tools.js' });
  vm.runInThisContext(view, { filename: 'chat-view.js' });
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

// ── F3 MD 渲染集成——text 整块经 chat-md.js 渲染为 HTML（md-block 包裹）──
test('F3 text 整块 MD 渲染——表格/代码块/标题真实 DOM', () => {
  const md = '## 标题\n\n| 列A | 列B |\n| --- | --- |\n| 1 | 2 |\n\n```\ncode\n```';
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: md }, replaceSeq: -1 });
  const bubble = bubbles()[0];
  // md-block 作用域锚点存在
  const block = bubble.querySelector('.md-block');
  expect(block).not.toBeNull();
  // 标题 → h2
  expect(block.querySelector('h2').textContent).toBe('标题');
  // 表格 → 真 table（thead + tbody）
  const table = block.querySelector('table');
  expect(table).not.toBeNull();
  expect(table.querySelector('thead')).not.toBeNull();
  expect(table.querySelector('tbody tr').textContent).toBe('12');
  // 代码块 → pre > code
  expect(block.querySelector('pre code').textContent).toBe('code');
  // 纯文本内容 textContent 仍完整（无障碍可读）
  expect(bubble.textContent).toContain('标题');
});

test('F3 user 气泡保持纯文本（不 MD 渲染）', () => {
  window.chatOnView({ seq: 1, renderType: 'user', payload: { content: '## 不是标题', source: 'user' }, replaceSeq: -1 });
  expect(bubbles()[0].querySelector('.md-block')).toBeNull();
  expect(bubbles()[0].textContent).toBe('## 不是标题');
});

// ── P6b 节点操作条——text 块带 msgIndex 渲染两按钮（回滚/分支）；无 msgIndex 不渲染 ──
test('P6b text 块带 msgIndex 渲染节点操作条', () => {
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: '回复内容', msgIndex: 3 }, replaceSeq: -1 });
  const bubble = bubbles()[0];
  const bar = bubble.querySelector('.node-actions');
  expect(bar).not.toBeNull();
  expect(bar.querySelectorAll('.node-btn').length).toBe(2);
  expect(bubble.querySelector('.node-btn-rollback').title).toContain('从此处继续对话');
  expect(bubble.querySelector('.node-btn-fork').title).toContain('新建独立 Cat');
});

test('P6b text 块无 msgIndex（工具轮 seal/旧数据）不渲染操作条', () => {
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: '工具轮文本' }, replaceSeq: -1 });
  expect(bubbles()[0].querySelector('.node-actions')).toBeNull();
  window.chatOnView({ seq: 21, renderType: 'text', payload: { content: '负索引', msgIndex: -1 }, replaceSeq: -1 });
  expect(bubbles()[1].querySelector('.node-actions')).toBeNull();
});


// ── reason 整块 ──
test('view reason 整块替换思考流式容器', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '最终思考' }, replaceSeq: 11 });
  expect(window.viewContainers[11]).toBeUndefined();
  const reasonDetail = chatMsgs.querySelector('.chat-reason div');
  expect(reasonDetail.textContent).toBe('最终思考');
});

// ── reason 流式展开 + 整块折叠 + summary 字数 ──
test('reason 流式展开增量可见——整块替换后折叠（summary 字数同步）', () => {
  // 流式创建——展开（增量可见）+ 流式同步字数
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  let det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(true);
  expect(det.querySelector('summary').textContent).toContain('Thinking · 2');
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '中' }, replaceSeq: -1 });
  expect(det.querySelector('summary').textContent).toContain('Thinking · 3');
  // 整块替换——完成后折叠（字数同步）
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '最终思考内容' }, replaceSeq: 11 });
  det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toBe('Think：最终思考内容');
  // 历史重建——完成态折叠 + 字数
  window.chatRenderHistory({
    version: 1, sessionId: 's1', count: 1,
    blocks: [{ seq: 1, id: '1:h1', renderType: 'reason', payload: { content: '历史思考' } }]
  });
  det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toBe('Think：历史思考');
});

// ── 思考结束即折叠（2026-09-17 规格变更——原「最终回复前最后一块保持展开」退役）──
test('思考流式 → reply 流式开始即折叠（不等整块、不等最终回复）', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  let det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(true);
  // reply 流式开始——思考块立即折叠（summary 走折叠摘要渲染）
  window.chatOnView({ seq: 12, renderType: 'stream', payload: { kind: 'text', text: '回复' }, replaceSeq: -1 });
  det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toBe('Think：思考');
});

test('实时——最终回复（msgIndex≥0）后全部 reason 折叠', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '一轮思考' }, replaceSeq: -1 });
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '一轮思考' }, replaceSeq: 11 });
  window.chatOnView({ seq: 30, renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' }, replaceSeq: -1 });
  window.chatOnView({ seq: 12, renderType: 'stream', payload: { kind: 'reasoning', text: '二轮思考' }, replaceSeq: -1 });
  window.chatOnView({ seq: 22, renderType: 'reason', payload: { content: '二轮思考' }, replaceSeq: 12 });
  window.chatOnView({ seq: 40, renderType: 'text', payload: { content: '最终回复', msgIndex: 3 }, replaceSeq: -1 });
  const dets = chatMsgs.querySelectorAll('details.chat-reason');
  expect(dets.length).toBe(2);
  expect(dets[0].open).toBe(false);
  expect(dets[1].open).toBe(false);
});

test('历史重建——全部 reason 折叠（与实时一致）', () => {
  window.chatRenderHistory({
    version: 1, sessionId: 's-open', count: 2,
    blocks: [
      { seq: 1, id: '1:r', renderType: 'reason', payload: { content: '中间思考' } },
      { seq: 2, id: '2:t', renderType: 'text', payload: { content: '工具轮文本', msgIndex: -1 } },
      { seq: 3, id: '3:r', renderType: 'reason', payload: { content: '最终思考' } },
      { seq: 4, id: '4:t', renderType: 'text', payload: { content: '最终回复', msgIndex: 3 } }
    ]
  });
  const dets = chatMsgs.querySelectorAll('details.chat-reason');
  expect(dets.length).toBe(2);
  expect(dets[0].open).toBe(false);
  expect(dets[1].open).toBe(false);
});

// ── 展开态整块点击收起（2026-09-18）——原生 details 仅折叠头可点；展开后整块任意位置点击即收起 ──
test('展开态整块点击收起——工具卡内容区点击即折叠（折叠态不误触）', () => {
  window.chatOnView({
    seq: 40, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-09-18 00:00:00' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  card.open = true;
  card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
  // 折叠态再点内容区——保持在折叠（无异常、不反向展开）
  card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});

test('展开态整块点击收起——思考块内容区点击即折叠（summary 回折叠摘要）', () => {
  window.chatOnView({
    seq: 41, renderType: 'reason',
    payload: { content: '第一行\n第二行\n第三行' },
    replaceSeq: -1
  });
  const det = chatMsgs.querySelector('details.chat-reason');
  det.open = true;
  det._updateSummary();
  det.querySelector('div').dispatchEvent(new Event('click', { bubbles: true }));
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toContain('Think');
});

test('展开态整块点击收起——拖选文本（按下时已有选区）不收起，选区清空后恢复收起', () => {
  window.chatOnView({
    seq: 42, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: 'x' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  card.open = true;
  const original = window.getSelection;
  window.getSelection = function () {
    return { isCollapsed: false, toString: function () { return '选中内容'; } };
  };
  try {
    card.querySelector('.tr').dispatchEvent(new Event('mousedown', { bubbles: true }));
    card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
    expect(card.open).toBe(true);
  } finally {
    if (original === undefined) { delete window.getSelection; } else { window.getSelection = original; }
  }
  card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});

test('展开态整块点击收起——按下/抬起位移超 20px（拖选）不收起，小位移点击正常收起', () => {
  window.chatOnView({
    seq: 43, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: 'x' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  const tr = card.querySelector('.tr');
  const M = document.defaultView.MouseEvent;
  card.open = true;
  // 拖选——位移 45px 超限 → 不收起
  tr.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 100, clientY: 50 }));
  tr.dispatchEvent(new M('click', { bubbles: true, clientX: 145, clientY: 50 }));
  expect(card.open).toBe(true);
  // 普通点击——位移 3px 在限内 → 收起
  tr.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 100, clientY: 50 }));
  tr.dispatchEvent(new M('click', { bubbles: true, clientX: 103, clientY: 52 }));
  expect(card.open).toBe(false);
});

test('展开态整块点击收起——按下时长超 300ms（长按）不收起', () => {
  window.chatOnView({
    seq: 44, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: 'x' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  const tr = card.querySelector('.tr');
  const M = document.defaultView.MouseEvent;
  card.open = true;
  const realNow = Date.now;
  let fakeNow = realNow();
  Date.now = function () { return fakeNow; };
  try {
    tr.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 100, clientY: 50 }));
    fakeNow += 500;
    tr.dispatchEvent(new M('click', { bubbles: true, clientX: 100, clientY: 50 }));
    expect(card.open).toBe(true);
  } finally {
    Date.now = realNow;
  }
  // 时长恢复（快速点击）→ 收起
  tr.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 100, clientY: 50 }));
  tr.dispatchEvent(new M('click', { bubbles: true, clientX: 100, clientY: 50 }));
  expect(card.open).toBe(false);
});

test('展开态整块点击收起——内部按钮/折叠头点击不收起', () => {
  const det = document.createElement('details');
  det.open = true;
  const sum = document.createElement('summary');
  det.appendChild(sum);
  const btn = document.createElement('button');
  det.appendChild(btn);
  document.body.appendChild(det);
  window.chatBindBodyCollapse(det);
  btn.dispatchEvent(new Event('click', { bubbles: true }));
  expect(det.open).toBe(true);
  sum.dispatchEvent(new Event('click', { bubbles: true }));
  expect(det.open).toBe(true);
  det.remove();
});

// ── toolcard 整块 ──
test('view toolcard 渲染工具卡（含名称/参数/结果；默认折叠）', () => {
  window.chatOnView({
    seq: 30, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-08-30 00:00:00' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card).not.toBeNull();
  // details 结构——默认折叠（点击 summary 展开）
  expect(card.tagName).toBe('DETAILS');
  expect(card.open).toBe(false);
  expect(card.querySelector('.tn').textContent).toContain('时间');
  expect(card.querySelector('.tr').textContent).toBe('2026-08-30 00:00:00');
  // 展开后内容可见
  card.open = true;
  expect(card.querySelector('.tr').textContent).toBe('2026-08-30 00:00:00');
});

// ── toolcard 并发编号——对齐 CH2：并发批次 [icon n/m]；单次保持现状 ──
test('toolcard 并发批次渲染 [icon n/m] 前缀——单次用类型图标（⚠️ 失败态不变）', () => {
  // 并发批（1/4）——icon 按工具类型映射（text-read → 📖）
  window.chatOnView({
    seq: 30, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'OK', toolIndex: 1, toolTotal: 4 },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[0].textContent).toBe('[📖 1/4] 读取 (未指定) · 1 行 2 字符');
  // 单次（无 toolTotal 字段——旧数据兼容）——同样用类型图标（text-read → 📖）
  window.chatOnView({
    seq: 31, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'OK' },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[1].textContent).toBe('📖 读取 (未指定) · 1 行 2 字符');
  // 错误并发——[⚠️ n/m] 前缀
  window.chatOnView({
    seq: 32, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'ERR|X', toolIndex: 2, toolTotal: 4 },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[2].textContent).toBe('[⚠️ 2/4] 读取 (未指定) · 1 行 5 字符');
});

// ── powershell 命令解读——折叠行覆盖宿主 summary + 展开区意图块（chat-cmd.js 接入）──
test('toolcard powershell——命令解读覆盖 summary + 展开区意图块', () => {
  window.chatOnView({
    seq: 33, renderType: 'toolcard',
    payload: {
      name: 'powershell',
      arguments: JSON.stringify({ command: 'dotnet build CatHome4.sln; git status' }),
      result: '{"exit":0}',
      summary: '执行命令 "dotnet build CatHome4.sln; git status" → ...'
    },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  // 折叠行——宿主原始命令截断被解读结果覆盖 + PS 版本标签（双线区分，2026-09-18）
  expect(card.querySelector('.tn').textContent).toBe('💻 PS 5.1 dotnet build · 编译 C# 项目 「CatHome4.sln」 等 2 段');
  // 展开区首块——逐段意图（含指令类标识 tag）
  const intent = card.querySelector('.cmd-intent');
  expect(intent).not.toBeNull();
  expect(intent.textContent).toContain('1. dotnet build · 编译 C# 项目 「CatHome4.sln」');
  expect(intent.textContent).toContain('2. git status · 查看仓库状态');
  // 原文块保留（意图块之后）
  expect(card.querySelector('.ta').textContent).toContain('dotnet build');
});

// ── 非 powershell 工具——不进解读（宿主 summary 原样展示）──
test('toolcard 非 powershell——不插命令意图块', () => {
  window.chatOnView({
    seq: 34, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{"path":"a.txt"}', result: 'OK', summary: '读取文件 "a.txt" → "OK"' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 a.txt · 1 行 2 字符');
  expect(card.querySelector('.cmd-intent')).toBeNull();
});

// ── 结果规模标注——消耗可见（大结果标字符数 / 截断标上限 + 警示块 / 小结果不标）──
test('toolcard 大结果——折叠行标注字符数', () => {
  window.chatOnView({
    seq: 40, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{"path":"big.txt"}', result: 'x'.repeat(2000), summary: '读取文件 "big.txt" → ...' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 big.txt · 1 行 2.00k 字符');
  expect(card.querySelector('.ta.warn')).toBeNull();
});

test('toolcard 小结果——不标注（避免噪音）', () => {
  window.chatOnView({
    seq: 41, renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-09-14 17:00:00' },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelector('.chat-tool .tn').textContent).toBe('📝 时间  · 1 行 19 字符');
});

test('toolcard ps 结果截断——折叠行上限标注 + 展开区警示块', () => {
  const psResult = JSON.stringify({ exit: 0, stdout: 'y'.repeat(16384), stderr: '', truncated: true, timeout: false });
  window.chatOnView({
    seq: 42, renderType: 'toolcard',
    payload: { name: 'powershell', arguments: JSON.stringify({ command: 'Get-ChildItem -Recurse' }), result: psResult },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toContain('⚠️ 已达上限 16.38k 字符');
  const warn = card.querySelector('.ta.warn');
  expect(warn).not.toBeNull();
  expect(warn.textContent).toContain('被截断');
});

test('toolcard ps 正常结果——解析 stdout 长度标注', () => {
  const psResult = JSON.stringify({ exit: 0, stdout: 'z'.repeat(1500), stderr: '', truncated: false, timeout: false });
  window.chatOnView({
    seq: 43, renderType: 'toolcard',
    payload: { name: 'powershell', arguments: JSON.stringify({ command: 'Get-Date' }), result: psResult },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toContain('· 1.50k 字符');
  expect(card.querySelector('.ta.warn')).toBeNull();
});

// ── 工具轮 seal——残留文本流式容器闪烁标记移除（toolcard 到达兜底；宿主 seal 缺失防线）──
test('toolcard 到达——残留 text 流式容器 streaming 移除', () => {
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '中间文本' }, replaceSeq: -1 });
  const tb = bubbles()[0];
  expect(tb.classList.contains('streaming')).toBe(true);
  window.chatOnView({ seq: 30, renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' }, replaceSeq: -1 });
  expect(tb.classList.contains('streaming')).toBe(false);
  expect(window.viewContainers[10]).not.toBeUndefined();
});

// ── 工具卡两段式（2026-09-16）——先行"进行中"卡 + 完成/中断原位替换 ──
test('toolcard 两段式——先行卡（无 result）显示 ⏳ 处理中 + pending 标记', () => {
  window.chatOnView({
    seq: 50, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }), toolIndex: 1, toolTotal: 1 },
    replaceSeq: -1
  });
  expect(bubbles().length).toBe(1);
  const tb = bubbles()[0];
  expect(tb.classList.contains('pending')).toBe(true);
  const card = tb.querySelector('.chat-tool');
  expect(card.open).toBe(true);
  expect(card.querySelector('.tn').textContent).toContain('读取 a.txt');
  const tas = card.querySelectorAll('.ta');
  expect(tas[tas.length - 1].textContent).toContain('⏳ 处理中…');
  expect(card.querySelector('.tr')).toBeNull();
  expect(window.viewContainers['toolcard_50']).not.toBeUndefined();
});

test('toolcard 两段式——完成卡以 replaceSeq 原位替换（气泡不新增，pending 标记移除）', () => {
  window.chatOnView({
    seq: 50, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }), toolIndex: 1, toolTotal: 1 },
    replaceSeq: -1
  });
  window.chatOnView({
    seq: 51, renderType: 'toolcard',
    payload: { name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }), result: '文件内容', summary: '读取文件 a.txt', toolIndex: 1, toolTotal: 1 },
    replaceSeq: 50
  });
  expect(bubbles().length).toBe(1);
  const tb = bubbles()[0];
  expect(tb.classList.contains('pending')).toBe(false);
  const card = tb.querySelector('.chat-tool');
  expect(card.open).toBe(false);
  expect(card.querySelector('.tr').textContent).toBe('文件内容');
  expect(window.viewContainers['toolcard_50']).toBeUndefined();
});

// ── 新思考容器创建——残留文本流式容器闪烁标记移除（多轮工具循环残留兜底）──
test('新 reasoning 容器创建——残留 text 流式容器 streaming 移除', () => {
  window.chatOnView({ seq: 10, renderType: 'stream', payload: { kind: 'text', text: '前轮文本' }, replaceSeq: -1 });
  const tb = bubbles()[0];
  window.chatOnView({ seq: 21, renderType: 'stream', payload: { kind: 'reasoning', text: '新一轮思考' }, replaceSeq: -1 });
  expect(tb.classList.contains('streaming')).toBe(false);
  // 同 seq 续流不清理（reasoning 容器本身不受影响）
  expect(window.viewContainers[21].type).toBe('reason');
});

// ── control usage ──
test('view control usage 渲染 Token 统计', () => {
  window.chatOnView({ seq: 40, renderType: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } }, replaceSeq: -1 });
  expect(chatStatus.textContent).toContain('↑100');
  expect(chatStatus.textContent).toContain('↓20');
  expect(chatStatus.textContent).toContain('cache 30');
});

// ── error 视图（A55——独立 renderType：恒定独立错误气泡；已有容器只 seal 不追加）──
test('view error 有流式容器——seal 已有容器 + 独立错误气泡 + 恢复 idle', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'text', text: '部分' }, replaceSeq: -1 });
  const tb = bubbles()[0];
  window.chatOnView({ seq: 41, renderType: 'error', payload: { type: 'error', text: 'LLM 挂了' }, replaceSeq: -1 });
  expect(tb.textContent).toBe('部分');
  expect(tb.classList.contains('streaming')).toBe(false);
  expect(tb.classList.contains('error')).toBe(false);
  expect(bubbles().length).toBe(2);
  expect(bubbles()[1].textContent).toContain('LLM 挂了');
  expect(bubbles()[1].classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
  expect(Object.keys(window.viewContainers).length).toBe(0);
});

// ── A55 吞错修复：无流式容器时错误必须可见（新建错误气泡）──
test('view error 无容器——新建错误气泡', () => {
  window.chatOnView({ seq: 42, renderType: 'error', payload: { type: 'error', text: '连接超时' }, replaceSeq: -1 });
  expect(bubbles().length).toBe(1);
  expect(bubbles()[0].textContent).toContain('连接超时');
  expect(bubbles()[0].classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
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

// ── A55：error / retry 块随 view.json 落盘 → history 重建（渲染单例共用同一渲染面）──
test('chatRenderHistory 渲染 error 块（A55 持久化面）', () => {
  window.chatRenderHistory({
    version: 1,
    sessionId: 's-err',
    count: 2,
    blocks: [
      { seq: 1, id: '1:u', renderType: 'user', payload: { content: '问题' } },
      { seq: 2, id: '2:e', renderType: 'error', payload: { type: 'error', text: 'LLM 错误：限速' } }
    ],
    stats: { prompt: 0, cacheHit: 0, completion: 0 }
  });
  const errBubble = bubbles()[1];
  expect(errBubble.textContent).toContain('LLM 错误：限速');
  expect(errBubble.classList.contains('error')).toBe(true);
});

test('chatRenderHistory 渲染 retry 块（A55 持久化面）', () => {
  window.chatRenderHistory({
    version: 1,
    sessionId: 's-retry',
    count: 1,
    blocks: [
      { seq: 1, id: '1:r', renderType: 'retry', payload: { state: 'resolved', attempt: 2, max: 3 } }
    ],
    stats: { prompt: 0, cacheHit: 0, completion: 0 }
  });
  const rb = bubbles()[0];
  expect(rb.textContent).toContain('已恢复');
  expect(rb.classList.contains('resolved')).toBe(true);
});

// ── A59：六态状态条——数据源 = SSE sessionstate 推送（计时单源在后端；前端零轮询、零自算）──
test('A59 六态状态条——sessionstate 驱动渲染（六态时长 + 当前态高亮 + 请求次数）', () => {
  // 发送——清运行态（数据源 = SSE sessionstate，无前端轮询）
  window.chatState = 'idle';
  window.chatInput.value = '你好';
  window.chatSend();
  expect(window.chatPending.length).toBe(1);
  expect(window.chatState).toBe('sending');
  expect(window.chatRunState.state).toBe('');
  // 服务端推送本猫运行态块——六态时长 + 当前态 run + 请求次数
  window.chatOnSessionState({ sessionId: 'majordomo', runState: 'run', runMs: { link: 320, wait: 480, think: 28500, tool: 773, run: 11449, reply: 2400 }, requests: 5 });
  const bar = chatStatus;
  expect(bar.textContent).toContain('🔗 Link 0.3s');
  expect(bar.textContent).toContain('⏳ Wait 0.5s');
  expect(bar.textContent).toContain('🧠 Think 28.5s');
  expect(bar.textContent).toContain('🔧 Tool 0.8s');
  expect(bar.textContent).toContain('⚙️ Run 11.4s');
  expect(bar.textContent).toContain('💬 Reply 2.4s');
  expect(bar.textContent).toContain('⏱ All 43.9s');
  expect(bar.textContent).toContain('🔄 Api 5');
  // 当前态高亮（呼吸动效类）
  expect(bar.querySelector('.st.run').classList.contains('active')).toBe(true);
  // 畸形载荷（无 sessionId）——忽略（状态条不动）
  window.chatOnSessionState({ frame: 102 });
  expect(bar.textContent).toContain('⚙️ Run 11.4s');
  // 轮终态——清运行态（六态段清空）
  window.chatPhaseReset();
  expect(window.chatRunState.state).toBe('');
  expect(bar.textContent).not.toContain('⚙️ Run');
  expect(bar.textContent).not.toContain('🔄 Api');
});

// ── A61：底部行精简（去会话级统计）+ 轮结束整行清空 + 刷新后运行态恢复 sending ──
test('A61 底部行——只留本轮 Token（会话级统计不再渲染）', () => {
  window.chatOnView({
    seq: 60, renderType: 'control',
    payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30, sessionPrompt: 5000, sessionCompletion: 1200, sessionCacheHit: 800 } },
    replaceSeq: -1
  });
  const bar = chatStatus;
  expect(bar.textContent).toContain('↑100');
  expect(bar.textContent).toContain('miss 70');
  expect(bar.textContent).not.toContain('会话 ↑');
});

test('A61 轮结束——底部态与统计一并消失（空行）', () => {
  window.chatOnSessionState({ sessionId: 'majordomo', runState: 'run', runMs: { link: 320, think: 28500, run: 11449 }, requests: 5 });
  window.chatOnView({ seq: 61, renderType: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } }, replaceSeq: -1 });
  const bar = chatStatus;
  expect(bar.textContent.length).toBeGreaterThan(0);
  // 轮结束（chatdone / paused / 失败统一走 chatPhaseReset）——本轮统计已由 roundsum 气泡承载
  window.chatPhaseReset();
  expect(bar.textContent).toBe('');
  expect(bar.querySelectorAll('.st').length).toBe(0);
});

test('A61 刷新兜底——运行态首帧到达即恢复 sending（停止按钮可用）', () => {
  window.chatState = 'idle';
  window.chatSetState('idle');
  const pauseBtn = document.getElementById('chatPause');
  expect(pauseBtn.disabled).toBe(true);
  // 新连接首帧运行态（后端轮次在跑）——前端恢复 sending 面
  window.chatOnSessionState({ sessionId: 'majordomo', runState: 'think', runMs: { think: 5000 }, requests: 2 });
  expect(window.chatState).toBe('sending');
  expect(pauseBtn.disabled).toBe(false);
  // 空闲态推送不误置 sending（降级仍由 view 终态事件驱动）
  window.chatOnSessionState({ sessionId: 'majordomo', runState: 'idle', runMs: {}, requests: 0 });
  expect(window.chatState).toBe('sending');
});

// ── A59：状态条零值边界——非活跃零值态跳过、活跃零值态仍显示、requests=0 不显示 ──
test('A59 状态条零值边界——零值态跳过 + 当前态零值仍显示', () => {
  window.chatRunState = { state: 'link', ms: { link: 0, wait: 0, think: 0, tool: 0, run: 0, reply: 0 }, requests: 0 };
  window.chatRenderStatus();
  const bar = chatStatus;
  expect(bar.textContent).toContain('🔗 Link 0.0s');
  expect(bar.textContent).not.toContain('Think');
  expect(bar.textContent).not.toContain('Run');
  expect(bar.textContent).not.toContain('⏱ All');
  expect(bar.textContent).not.toContain('🔄');
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

// ── Note 面板（F2.4 补盲区——chat-note.js；Q3 重构：悬浮气泡 + 居中 modal）──
test('Note 面板：空态/渲染三态 + 展开收起', () => {
  // 空态
  window.noteState = { tasks: [], current: 0, done: 0, justCompleted: false };
  window.noteRender();
  expect(document.getElementById('noteTitle').textContent).toBe('Note · 空');
  expect(document.getElementById('noteBubbleText').textContent).toBe('Note · 空');
  // 三态渲染
  window.noteState = { tasks: ['任务A', '任务B', '任务C'], current: 1, done: 1 };
  window.noteRender();
  expect(document.getElementById('noteTitle').textContent).toBe('Note (2/3)');
  expect(document.getElementById('noteBubbleText').textContent).toContain('Note 2/3');
  const rows2 = document.querySelectorAll('#noteList .note-row');
  expect(rows2.length).toBe(3);
  expect(rows2[0].classList.contains('done')).toBe(true);
  expect(rows2[1].classList.contains('current')).toBe(true);
  // 展开收起（popover 显示/隐藏）
  window.noteModalOpen = false;
  window.noteToggle();
  expect(document.getElementById('notePopover').style.display).toBe('block');
  window.noteToggle();
  expect(document.getElementById('notePopover').style.display).toBe('none');
});

test('Note 完成态——justCompleted 显示 🎉 全部完成（Q7 外观层匹配）', () => {
  window.noteState = { tasks: [], current: 0, done: 0, justCompleted: true };
  window.noteRender();
  expect(document.getElementById('noteBubbleText').textContent).toBe('🎉 全部完成');
  expect(document.getElementById('noteTitle').textContent).toBe('Note · 全部完成');
});

// ── Note 展开态只由点击控制（2026-09-17 规格变更——原「计划存在自动展开」退役）──
test('Note 事件——计划存在不自动展开（展开仅由点击控制）', () => {
  window.noteModalOpen = false;
  document.getElementById('notePopover').style.display = 'none';
  window.noteOnEvent({ state: { tasks: ['计划'], current: 0, done: 0 } });
  expect(window.noteState.tasks.length).toBe(1);
  expect(window.noteModalOpen).toBe(false);
  expect(document.getElementById('notePopover').style.display).toBe('none');
  expect(document.getElementById('noteBubbleText').textContent).toContain('Note 1/1');
  // 点击仍可展开（点击是唯一控制面）
  window.noteToggle();
  expect(document.getElementById('notePopover').style.display).toBe('block');
  window.noteToggle();
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

// ── S2 §8.4 retry 独立视图条目 ──
test('view retry 新建重试气泡（⟳ 重试中 N/3）', () => {
  window.chatOnView({ seq: 50, renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: 'ERR|TRANSPORT|模拟抖动' }, replaceSeq: -1 });
  expect(rows().length).toBe(1);
  const rb = bubbles()[0];
  expect(rb.classList.contains('retry')).toBe(true);
  expect(rb.textContent).toContain('⟳ 重试中 1/3');
  expect(rb.textContent).toContain('模拟抖动');
});

test('view retry replaceSeq 更新同一气泡（多次重试不堆叠）', () => {
  window.chatOnView({ seq: 50, renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: '首次失败' }, replaceSeq: -1 });
  const first = bubbles()[0];
  // 第二次重试——replaceSeq 命中已有气泡 → 文本更新不新建
  window.chatOnView({ seq: 51, renderType: 'retry', payload: { state: 'retrying', attempt: '2', max: '3', text: '再次失败' }, replaceSeq: 50 });
  expect(rows().length).toBe(1);
  expect(bubbles()[0]).toBe(first);
  expect(bubbles()[0].textContent).toContain('⟳ 重试中 2/3');
});

test('view retry resolved 状态切换（✓ 已恢复）', () => {
  window.chatOnView({ seq: 50, renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: '失败' }, replaceSeq: -1 });
  const first = bubbles()[0];
  window.chatOnView({ seq: 52, renderType: 'retry', payload: { state: 'resolved', text: '已恢复' }, replaceSeq: 50 });
  expect(rows().length).toBe(1);
  expect(bubbles()[0]).toBe(first);
  expect(bubbles()[0].textContent).toContain('✓ 已恢复');
  expect(bubbles()[0].classList.contains('resolved')).toBe(true);
});

test('view retry 历史重建——retry 块渲染（chatRenderHistory）', () => {
  window.chatRenderHistory({
    blocks: [
      { renderType: 'retry', payload: { state: 'retrying', attempt: '2', max: '3', text: '历史失败' } },
      { renderType: 'retry', payload: { state: 'resolved', attempt: '2' } }
    ],
    sessionId: 's1',
    count: 2
  });
  expect(rows().length).toBe(2);
  expect(bubbles()[0].textContent).toContain('⟳ 重试中 2/3');
  expect(bubbles()[1].textContent).toContain('✓ 已恢复');
  expect(bubbles()[1].classList.contains('resolved')).toBe(true);
});

// ── 注入报告（问题二——前文加载明细持久化进视图）──
test('inject_report 历史首块渲染前文加载明细（ok/missing/error 三态）', () => {
  window.chatRenderHistory({
    blocks: [
      {
        renderType: 'inject_report',
        payload: {
          files: [
            { file: 'ccbp:L1/Tree.md', status: 'ok', message: '', chars: 1234 },
            { file: 'ccbp:L1/Missing.md', status: 'missing', message: '文件不存在（可选，已跳过）', chars: 0 },
            { file: 'ccbp:L1/Broken.md', status: 'error', message: '读取异常', chars: 0 }
          ],
          total: 3, ok: 1, missing: 1, failed: 1, injectCount: 3
        }
      }
    ],
    sessionId: 's1',
    count: 1
  });
  const ib = chatMsgs.querySelector('.chat-bubble.inject');
  expect(ib).not.toBeNull();
  expect(ib.textContent).toContain('前文加载：1/3 成功');
  expect(ib.textContent).toContain('✅ ccbp:L1/Tree.md');
  expect(ib.textContent).toContain('⚠️ ccbp:L1/Missing.md');
  expect(ib.textContent).toContain('❌ ccbp:L1/Broken.md');
});

// ── 会话重置（问题一——session_reset 显式事件消除清空竞态）──
test('control session_reset 清空气泡 + 重拉 history（chatPendingReset 消费）', async () => {
  // 前置——模拟新会话点击置位
  window.chatPendingReset = true;
  window.chatOnView({ seq: 1, renderType: 'user', payload: { content: '旧气泡', source: 'user' }, replaceSeq: -1 });
  expect(bubbles().length).toBe(1);
  // fetch mock——history 返回空（新会话无前文）
  const origFetch = window.fetch;
  window.fetch = function (url) {
    if (String(url).indexOf('/api/v1/history') >= 0) {
      return Promise.resolve({ json: function () { return Promise.resolve({ sessionId: 's2', count: 0, blocks: [], stats: { prompt: 0, cacheHit: 0, completion: 0 } }); } });
    }
    return Promise.resolve({ json: function () { return Promise.resolve({ ok: true }); } });
  };
  window.chatOnView({ seq: 2, renderType: 'control', payload: { type: 'session_reset' }, replaceSeq: -1 });
  await new Promise(function (r) { setTimeout(r, 20); });
  window.fetch = origFetch;
  expect(window.chatPendingReset).toBe(false);
  expect(bubbles().length).toBe(0);   // 旧气泡已清
  expect(chatInfo.textContent).toContain('前文 0 条');
});

// ── 会话终态统计（问题三——chatdone 带真实 usage；顶部栏只显示前文长度=context 单次值）──
test('control chatdone 显示前文真实 usage（前文长度 context）', () => {
  window.chatOnView({ seq: 1, renderType: 'stream', payload: { kind: 'text', text: '回复' }, replaceSeq: -1 });
  window.chatOnView({ seq: 2, renderType: 'control', payload: { type: 'chatdone', count: 5, stats: { prompt: 120, cacheHit: 40, completion: 30, context: 150 } }, replaceSeq: -1 });
  expect(window.chatState).toBe('idle');
  expect(chatInfo.textContent).toContain('前文 5 条');
  expect(chatInfo.textContent).toContain('前文 150 tokens');
  // 命中/非命中/输出归 roundsum 气泡——顶部栏不再显示
  expect(chatInfo.textContent).not.toContain('命中');
});

// ── 会话重置 miss 兜底（问题一——session_reset 丢失时 chatdone 重拉）──
test('control chatdone 在 chatPendingReset 置位时兜底重拉 history', async () => {
  window.chatPendingReset = true;
  const origFetch = window.fetch;
  window.fetch = function (url) {
    if (String(url).indexOf('/api/v1/history') >= 0) {
      return Promise.resolve({ json: function () { return Promise.resolve({ sessionId: 's3', count: 0, blocks: [], stats: { prompt: 0, cacheHit: 0, completion: 0 } }); } });
    }
    return Promise.resolve({ json: function () { return Promise.resolve({ ok: true }); } });
  };
  window.chatOnView({ seq: 1, renderType: 'control', payload: { type: 'chatdone', count: 1 }, replaceSeq: -1 });
  await new Promise(function (r) { setTimeout(r, 20); });
  window.fetch = origFetch;
  expect(window.chatPendingReset).toBe(false);
  expect(chatInfo.textContent).toContain('前文 0 条');
});

// ── view/control note 分支（宿主经 PushView 推送 Note 状态——前端转交 noteOnEvent 重绘）──
test('control note 分支——view 载荷更新 Note 面板（不自动展开）', () => {
  window.noteModalOpen = false;
  document.getElementById('notePopover').style.display = 'none';
  window.chatOnView({ seq: 1, renderType: 'control', payload: { type: 'note', state: { tasks: ['前端任务'], current: 0, done: 0 } }, replaceSeq: -1 });
  expect(window.noteState.tasks.length).toBe(1);
  expect(window.noteState.tasks[0]).toBe('前端任务');
  // 展开态只由点击控制——状态更新不自动展开 popover（2026-09-17 规格变更）
  expect(document.getElementById('notePopover').style.display).toBe('none');
  expect(window.noteModalOpen).toBe(false);
  // 面板标题已重绘
  expect(document.getElementById('noteTitle').textContent).toBe('Note (1/1)');
});

// ── 改动三：大数 k/M 格式化 ──
test('chatFmtCount 大数格式化——k/M 边界', () => {
  expect(window.chatFmtCount(48)).toBe('48');
  expect(window.chatFmtCount(999)).toBe('999');
  expect(window.chatFmtCount(1000)).toBe('1.00k');
  expect(window.chatFmtCount(435652)).toBe('435.65k');
  expect(window.chatFmtCount(363008)).toBe('363.01k');
  expect(window.chatFmtCount(72644)).toBe('72.64k');
  expect(window.chatFmtCount(12158)).toBe('12.16k');
  expect(window.chatFmtCount(1000000)).toBe('1.00M');
  expect(window.chatFmtCount(1234567)).toBe('1.23M');
});

test('chatdone 大数 usage 文本——k/M 格式化生效（context 前文长度）', () => {
  window.chatOnView({ seq: 1, renderType: 'control', payload: { type: 'chatdone', count: 48, stats: { prompt: 435652, cacheHit: 363008, completion: 12158, context: 151234 } }, replaceSeq: -1 });
  expect(chatInfo.textContent).toContain('前文 48 条');
  expect(chatInfo.textContent).toContain('前文 151.23k tokens');
});

test('chatRenderHistory 大数 usage 文本——k/M 格式化生效（context 前文长度）', () => {
  window.chatRenderHistory({
    version: 1,
    sessionId: 's-big',
    count: 48,
    blocks: [],
    stats: { prompt: 1000000, cacheHit: 200000, completion: 1500000, context: 998877 }
  });
  expect(chatInfo.textContent).toContain('前文 48 条');
  expect(chatInfo.textContent).toContain('前文 998.88k tokens');
});

// ── 改动四：roundsum 轮末统计气泡（A59——六态用时 + 请求次数）──
test('view roundsum 渲染独立气泡（token + 工具/请求次数 + 六态用时 + 总耗时）', () => {
  window.chatOnView({ seq: 1, renderType: 'roundsum', payload: { type: 'roundsum', data: { prompt: 769770, completion: 3550, cacheHit: 765700, miss: 4070, toolCount: 6, requests: 5, elapsedMs: 76200, phases: { wait: 480, link: 200, think: 28500, tool: 773, run: 45100, reply: 2400 } } }, replaceSeq: -1 });
  const rs = chatMsgs.querySelector('.chat-bubble.roundsum');
  expect(rs).not.toBeNull();
  expect(rs.textContent).toContain('Round');
  expect(rs.textContent).toContain('↑769.77k');
  expect(rs.textContent).toContain('↓3.55k');
  expect(rs.textContent).toContain('cache 765.70k');
  expect(rs.textContent).toContain('miss 4.07k');
  expect(rs.textContent).toContain('🎯99.5%');
  expect(rs.textContent).toContain('🔧 Tool 6 · 🔄 Api 5');
  expect(rs.textContent).toContain('Link 0.2s');
  expect(rs.textContent).toContain('Wait 0.5s');
  expect(rs.textContent).toContain('Think 28.5s');
  expect(rs.textContent).toContain('Run 45.1s');
  expect(rs.textContent).toContain('Reply 2.4s');
  expect(rs.textContent).toContain('All 1m16.2s');
  // idle 不入载荷——不产生对应行
  expect(rs.textContent).not.toContain('空闲');
});

test('chatRenderHistory roundsum 块渲染（历史重建保留轮末统计）', () => {
  window.chatRenderHistory({
    version: 1,
    sessionId: 's-rs',
    count: 3,
    blocks: [
      { seq: 1, id: '1:u', renderType: 'user', payload: { content: '问题' } },
      { seq: 2, id: '2:t', renderType: 'text', payload: { content: '回答' } },
      { seq: 3, id: '3:rs', renderType: 'roundsum', payload: { type: 'roundsum', data: { prompt: 1200, completion: 300, cacheHit: 1000, miss: 200, toolCount: 2, elapsedMs: 15000, phases: { link: 100, think: 5000, tool: 8000, reply: 1900 } } } }
    ],
    stats: { prompt: 1200, cacheHit: 1000, completion: 300, context: 1200 }
  });
  expect(chatMsgs.querySelectorAll('.chat-bubble.roundsum').length).toBe(1);
  const rs2 = chatMsgs.querySelector('.chat-bubble.roundsum');
  expect(rs2.textContent).toContain('↑1.20k');
  expect(rs2.textContent).toContain('🎯83.3%');
  expect(rs2.textContent).toContain('🔧 Tool 2');
  expect(rs2.textContent).toContain('All 15.0s');
  // 旧数据兼容——无 requests 字段时不显示请求段（历史块零迁移）
  expect(rs2.textContent).not.toContain('🔄 Api');
});

// ── 外观层优化：思考折叠摘要——首行 +（N行M字符已省略显示）+ 末行 ──
test('reason 折叠摘要——多行保留首尾行 + 省略行/字符计数（展开回退字数标题）', () => {
  const text = '第一行开始\n第二行\n第三行\n第四行\n最后一行结束';
  const det = window.chatReasonBlock(text, false);
  // 折叠——Think 标签 + 灰色缩略内容（首行 +（3行11字符已省略显示）+ 末行）
  expect(det.querySelector('summary').textContent).toBe('Think：第一行开始（3行11字符已省略显示）最后一行结束');
  expect(det.querySelector('summary .rs-peek')).not.toBeNull();
  // 展开——回退字数标题（jsdom 不触发 toggle——显式 _updateSummary；真实浏览器点击 toggle 自动同结果）
  det.open = true;
  det._updateSummary();
  expect(det.querySelector('summary').textContent).toBe('Thinking · 24');
  // 再折叠——回到折叠摘要
  det.open = false;
  det._updateSummary();
  expect(det.querySelector('summary').textContent).toBe('Think：第一行开始（3行11字符已省略显示）最后一行结束');
});

// ── 折叠摘要——少于 3 行不压缩，直接显示原文（2026-09-16）──
test('reason 折叠摘要——单行/两行直接显示原文，不回退字数', () => {
  const one = window.chatReasonBlock('这是一段很短的思考', false);
  expect(one.querySelector('summary').textContent).toBe('Think：这是一段很短的思考');
  const two = window.chatReasonBlock('第一行\n第二行', false);
  expect(two.querySelector('summary').textContent).toBe('Think：第一行\n第二行');
});

// ── 外观层优化：时长三级进位——秒/分/小时（hour 最高单位）──
test('chatFmtMs 三级进位——<60s 秒 / ≥60s 分 / ≥3600s 小时（单位英文）', () => {
  expect(window.chatFmtMs(5000)).toBe('5.0s');
  expect(window.chatFmtMs(61000)).toBe('1m1.0s');
  expect(window.chatFmtMs(3661000)).toBe('1h1m1.0s');
  expect(window.chatFmtMs(7200000)).toBe('2h0m0.0s');
});

// ── 外观层优化：状态条时长进位——秒/分三级进位（后端毫秒值驱动；时间走 chatFmtMs）──
test('A59 状态条时长进位——后端毫秒值走秒/分进位', () => {
  window.chatRunState = { state: '', ms: { link: 10000, think: 65000, run: 3000 }, requests: 0 };
  window.chatRenderStatus();
  expect(chatStatus.textContent).toContain('⏱ All');
  expect(chatStatus.textContent).toContain('1m18.0s');  // 10+65+3=78s
  expect(chatStatus.textContent).toContain('🧠 Think 1m5.0s');
  expect(chatStatus.textContent).toContain('🔗 Link 10.0s');
  expect(chatStatus.textContent).toContain('⚙️ Run 3.0s');
  // 清场——不把运行态留给后续用例
  window.chatPhaseReset();
});

// ── F6 竞态补齐（P20-P2-1 回归）——整块/工具卡到达 seal 残留 reason 流式容器 ──
test('F6 seal 补齐——工具卡与 text 整块到达后残留 reason 容器闪烁标记撤除', () => {
  // 场景一：思考流式 → 工具卡（多轮工具循环常见路径）
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考中' }, replaceSeq: -1 });
  const rb = chatMsgs.querySelector('.chat-reason').closest('.chat-bubble');
  expect(rb.classList.contains('streaming')).toBe(true);
  window.chatOnView({ seq: 30, renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' }, replaceSeq: -1 });
  expect(rb.classList.contains('streaming')).toBe(false);
  // 容器保留——仍须承接后续同 seq 流式或 replaceSeq 整块替换
  expect(window.viewContainers[11]).not.toBeUndefined();

  // 场景二：思考流式 → 回复 text 整块（无 reason 整块替换路径）
  window.chatOnView({ seq: 12, renderType: 'stream', payload: { kind: 'reasoning', text: '二轮思考' }, replaceSeq: -1 });
  const rb2 = chatMsgs.querySelectorAll('.chat-reason')[1].closest('.chat-bubble');
  expect(rb2.classList.contains('streaming')).toBe(true);
  window.chatOnView({ seq: 20, renderType: 'text', payload: { content: '最终回复' }, replaceSeq: -1 });
  expect(rb2.classList.contains('streaming')).toBe(false);
  expect(chatMsgs.querySelector('.md-block')).not.toBeNull();
});

// ═══════════════════════════════════════════
// A77 静默兜底（C 方案 · 2026-09-22）——只作明确展示：不改在途块状态 / 不清容器 / 不假收口
// ═══════════════════════════════════════════

test('A77 静默兜底——120s 静默贴提示，且不动在途容器状态（pending 保留 · 容器不清 · 仍 sending）', () => {
  vi.useFakeTimers();
  try {
    window.chatState = 'sending';
    // 先行工具卡（pending——蓝色呼吸态来源）
    window.chatOnToolCard(1, -1, { name: 'mau-setup', arguments: '{}', toolIndex: 1, toolTotal: 1 });
    const bubble = chatMsgs.querySelector('.chat-bubble.tool');
    expect(bubble.classList.contains('pending')).toBe(true);
    expect(window.chatTimer).toBeTruthy();
    vi.advanceTimersByTime(120000);
    const stall = chatMsgs.querySelector('.chat-bubble.stall');
    expect(stall).toBeTruthy();
    expect(stall.textContent).toContain('120s');
    // 在途状态不变——收口交后端（真实终态卡到达仍走原位替换）
    expect(bubble.classList.contains('pending')).toBe(true);
    expect(bubble.classList.contains('error')).toBe(false);
    expect(Object.keys(window.viewContainers).length).toBe(1);
    expect(window.chatState).toBe('sending');
  } finally {
    vi.useRealTimers();
  }
});

test('A77 静默兜底——非 sending 态不贴提示', () => {
  vi.useFakeTimers();
  try {
    window.chatState = 'idle';
    window.chatKeepAlive();
    vi.advanceTimersByTime(120000);
    expect(chatMsgs.querySelector('.chat-bubble.stall')).toBeNull();
  } finally {
    vi.useRealTimers();
  }
});

test('A77 静默提示——同一次静默只贴一条（去重）', () => {
  window.chatShowStall();
  window.chatShowStall();
  expect(chatMsgs.querySelectorAll('.chat-bubble.stall').length).toBe(1);
});

test('A77 静默提示——view 事件到达即撤销（宿主复活自愈）', () => {
  window.chatState = 'sending';
  window.chatShowStall();
  expect(chatMsgs.querySelector('.chat-bubble.stall')).toBeTruthy();
  window.chatOnView({ renderType: 'text', seq: -1, replaceSeq: -1, payload: { content: '已恢复', msgIndex: -1 } });
  expect(chatMsgs.querySelector('.chat-bubble.stall')).toBeNull();
});
