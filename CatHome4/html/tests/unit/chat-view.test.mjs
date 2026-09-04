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
  // F2.4 门禁迁移——加载外部模块（对话核心 + Note 面板 + F3 MD 解析器；不执行引导层 SSE/按钮绑定/初始化）
  const md = await readFile(new URL('../../js/chat-md.js', import.meta.url), 'utf-8');
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const note = await readFile(new URL('../../js/chat-note.js', import.meta.url), 'utf-8');
  vm.runInThisContext(md, { filename: 'chat-md.js' });
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


// ── reason 整块 ──
test('view reason 整块替换思考流式容器', () => {
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '最终思考' }, replaceSeq: 11 });
  expect(window.viewContainers[11]).toBeUndefined();
  const reasonDetail = chatMsgs.querySelector('.chat-reason div');
  expect(reasonDetail.textContent).toBe('最终思考');
});

// ── reason 默认折叠 + summary 字数 ──
test('reason 默认折叠——summary 显示字数（流式/整块/历史一致）', () => {
  // 流式创建——默认折叠 + 流式同步字数
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '思考' }, replaceSeq: -1 });
  let det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toContain('2 字');
  window.chatOnView({ seq: 11, renderType: 'stream', payload: { kind: 'reasoning', text: '中' }, replaceSeq: -1 });
  expect(det.querySelector('summary').textContent).toContain('3 字');
  // 整块替换——字数同步
  window.chatOnView({ seq: 21, renderType: 'reason', payload: { content: '最终思考内容' }, replaceSeq: 11 });
  det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toContain('6 字');
  // 历史重建——默认折叠 + 字数
  window.chatRenderHistory({
    version: 1, sessionId: 's1', count: 1,
    blocks: [{ seq: 1, id: '1:h1', renderType: 'reason', payload: { content: '历史思考' } }]
  });
  det = chatMsgs.querySelector('.chat-reason');
  expect(det.open).toBe(false);
  expect(det.querySelector('summary').textContent).toContain('4 字');
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
  expect(card.querySelector('.tn').textContent).toContain('time');
  expect(card.querySelector('.tr').textContent).toBe('2026-08-30 00:00:00');
  // 展开后内容可见
  card.open = true;
  expect(card.querySelector('.tr').textContent).toBe('2026-08-30 00:00:00');
  // 工具态
  expect(window.chatActivePhase).toBe('tool');
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
  expect(chatInfo.textContent).toContain('会话 0 条');
});

// ── 会话终态统计（问题三——chatdone 带真实 usage；顶部栏只显示前文长度=context 单次值）──
test('control chatdone 显示前文真实 usage（前文长度 context）', () => {
  window.chatOnView({ seq: 1, renderType: 'stream', payload: { kind: 'text', text: '回复' }, replaceSeq: -1 });
  window.chatOnView({ seq: 2, renderType: 'control', payload: { type: 'chatdone', count: 5, stats: { prompt: 120, cacheHit: 40, completion: 30, context: 150 } }, replaceSeq: -1 });
  expect(window.chatState).toBe('idle');
  expect(chatInfo.textContent).toContain('会话 5 条');
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
  expect(chatInfo.textContent).toContain('会话 0 条');
});

// ── 问题一修复：view/control note 分支（宿主经 PushView 推送 Note 状态——前端转交 noteOnEvent 重绘）──
test('control note 分支——view 载荷更新 Note 面板', () => {
  window.noteExpanded = false;
  document.getElementById('notePanel').classList.add('collapsed');
  window.chatOnView({ seq: 1, renderType: 'control', payload: { type: 'note', state: { tasks: ['前端任务'], current: 0, done: 0 } }, replaceSeq: -1 });
  expect(window.noteState.tasks.length).toBe(1);
  expect(window.noteState.tasks[0]).toBe('前端任务');
  // 计划存在自动展开
  expect(document.getElementById('notePanel').classList.contains('collapsed')).toBe(false);
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
  expect(chatInfo.textContent).toContain('会话 48 条');
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
  expect(chatInfo.textContent).toContain('会话 48 条');
  expect(chatInfo.textContent).toContain('前文 998.88k tokens');
});

// ── 改动四：roundsum 轮末统计气泡 ──
test('view roundsum 渲染独立气泡（token + 工具次数 + 四态用时 + 总耗时）', () => {
  window.chatOnView({ seq: 1, renderType: 'roundsum', payload: { type: 'roundsum', data: { prompt: 769770, completion: 3550, cacheHit: 765700, miss: 4070, toolCount: 6, elapsedMs: 76200, phases: { link: 200, think: 28500, tool: 45100, reply: 2400 } } }, replaceSeq: -1 });
  const rs = chatMsgs.querySelector('.chat-bubble.roundsum');
  expect(rs).not.toBeNull();
  expect(rs.textContent).toContain('本轮统计');
  expect(rs.textContent).toContain('↑769.77k');
  expect(rs.textContent).toContain('↓3.55k');
  expect(rs.textContent).toContain('cache 765.70k');
  expect(rs.textContent).toContain('miss 4.07k');
  expect(rs.textContent).toContain('🔧 工具 6 次');
  expect(rs.textContent).toContain('思考 28.5s');
  expect(rs.textContent).toContain('工具 45.1s');
  expect(rs.textContent).toContain('回复 2.4s');
  expect(rs.textContent).toContain('总计 1分16.2秒');
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
  expect(rs2.textContent).toContain('🔧 工具 2 次');
  expect(rs2.textContent).toContain('总计 15.0s');
});
