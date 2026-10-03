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
  const think = await readFile(new URL('../../js/chat-think.js', import.meta.url), 'utf-8');
  const view = await readFile(new URL('../../js/chat-view.js', import.meta.url), 'utf-8');
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const note = await readFile(new URL('../../js/chat-note.js', import.meta.url), 'utf-8');
  vm.runInThisContext(common, { filename: 'ui-common.js' });
  vm.runInThisContext(md, { filename: 'chat-md.js' });
  vm.runInThisContext(cmd, { filename: 'chat-cmd.js' });
  vm.runInThisContext(tools, { filename: 'chat-tools.js' });
  vm.runInThisContext(think, { filename: 'chat-think.js' });
  vm.runInThisContext(view, { filename: 'chat-view.js' });
  vm.runInThisContext(core, { filename: 'chat-core.js' });
  vm.runInThisContext(note, { filename: 'chat-note.js' });
  // A162 测试垫片——旧 op 面（live.* / persist.append）归一化为状态推送载荷（生产前端已退役旧 op，
  // 用例形态与覆盖面不变；渲染路径仍走 chatOnView → chatApplyBlock）
  const rawOnView = globalThis.chatOnView;
  globalThis.chatOnView = function (d) {
    if (d && d.op === 'live.remove') { return rawOnView({ op: 'delta', blocks: [], remove: [d.key] }); }
    if (d && (d.op === 'live.add' || d.op === 'live.update' || d.op === 'persist.append')) {
      return rawOnView({ op: 'delta', blocks: [d], remove: [] });
    }
    return rawOnView(d);
  };
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
  // A84/A85——活跃计时表清理（上一条用例遗留的秒表不跨用例）
  if (window.chatLiveTimer) { clearInterval(window.chatLiveTimer); window.chatLiveTimer = null; }
  chatMsgs.textContent = '';
  chatStatus.textContent = '';
  if (window.__clipboardWrites) { window.__clipboardWrites.length = 0; }
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
  window.chatOnView({ op: 'persist.append', renderType: 'user', payload: { content: '你好', source: 'user' } });
  expect(rows().length).toBe(1);
  expect(rows()[0].classList.contains('user')).toBe(true);
  expect(bubbles()[0].textContent).toBe('你好');

  window.chatOnView({ op: 'persist.append', renderType: 'user', payload: { content: '自动拉起', source: 'system' } });
  expect(rows().length).toBe(2);
  expect(bubbles()[1].textContent).toBe('[SystemAuto] 自动拉起');
  expect(bubbles()[1].classList.contains('system')).toBe(true);
});

// ── stream 流式容器（text）──
test('view stream text 创建回复容器并流式追加（同 seq 复用）', () => {
  // 新 seq → 新建容器
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '你' } });
  expect(rows().length).toBe(1);
  expect(window.viewContainers['live:10'].type).toBe('text');
  // 同 seq → 追加到同一容器
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '好' } });
  expect(bubbles()[0].textContent).toBe('你好');
  // 容器未删除——仍可继续追加
  expect(window.viewContainers['live:10']).not.toBeUndefined();
});

// ── stream 流式容器（reasoning · A85 think 流式态）──
test('view stream reasoning 创建 think 流式块并流式追加', () => {
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考' } });
  const c = window.viewContainers['live:11'];
  expect(c.type).toBe('thinkstream');
  expect(c.bubble.querySelector('.chat-think.stream')).not.toBeNull();
  expect(window.chatThinkBodyText(c.body)).toBe('思考');
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '中' } });
  expect(window.chatThinkBodyText(c.body)).toBe('思考中');
});

// ── text 整块替换流式容器 ──
test('A158 持久 text 块新建气泡（两区互不相识：流式块由后端 live.remove 退场）', () => {
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '流式' } });
  // 后端换手——同一处理点内先发持久块（新建气泡）再发流式区移除
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '最终整块' } });
  window.chatOnView({ op: 'live.remove', key: 'live:10' });
  expect(bubbles().length).toBe(1);
  expect(bubbles()[0].textContent).toBe('最终整块');
  expect(window.viewContainers['live:10']).toBeUndefined();
});

// ── text 整块无容器（无流式直接整块）──
test('view text 无命中容器时新建气泡', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '直接整块' } });
  expect(rows().length).toBe(1);
  expect(bubbles()[0].textContent).toBe('直接整块');
});

// ── F3 MD 渲染集成——text 整块经 chat-md.js 渲染为 HTML（md-block 包裹）──
test('F3 text 整块 MD 渲染——表格/代码块/标题真实 DOM', () => {
  const md = '## 标题\n\n| 列A | 列B |\n| --- | --- |\n| 1 | 2 |\n\n```\ncode\n```';
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: md } });
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
  window.chatOnView({ op: 'persist.append', renderType: 'user', payload: { content: '## 不是标题', source: 'user' } });
  expect(bubbles()[0].querySelector('.md-block')).toBeNull();
  expect(bubbles()[0].textContent).toBe('## 不是标题');
});

// ── A81 MD 块复制原文——按钮挂载与剪贴板写入 ──
test('A81 代码块挂复制按钮（零文本节点——不污染 textContent）', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '```\ncode\n```' } });
  const bubble = bubbles()[0];
  const wrap = bubble.querySelector('.md-copy');
  expect(wrap).not.toBeNull();
  const btn = wrap.querySelector('button.md-copy-btn');
  expect(btn).not.toBeNull();
  expect(btn.textContent).toBe('');            // 图标是元素（SVG），非文本节点
  expect(btn.querySelector('svg')).not.toBeNull();   // 线条复制图标（内联 SVG——非 emoji）
  expect(bubble.textContent).toBe('code');     // 按钮与源文本载体均不进 textContent
});

test('A81 表格复制按钮——点击写入剪贴板（源文本含分隔行）', async () => {
  const md = '| a | b |\n| --- | --- |\n| 1 | 2 |';
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: md } });
  const btn = bubbles()[0].querySelector('.md-copy-btn');
  btn.click();
  await new Promise((r) => setTimeout(r, 0));
  expect(window.__clipboardWrites).toEqual(['| a | b |\n| --- | --- |\n| 1 | 2 |']);
  expect(btn.classList.contains('done')).toBe(true);
});

test('A81 代码块复制内容——不含围栏', async () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '```\nx = 1\n```' } });
  const btn = bubbles()[0].querySelector('.md-copy-btn');
  btn.click();
  await new Promise((r) => setTimeout(r, 0));
  expect(window.__clipboardWrites).toEqual(['x = 1']);
});

test('A81 无代码块/表格的回复不挂按钮', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '纯文本回复' } });
  expect(bubbles()[0].querySelector('.md-copy-btn')).toBeNull();
});

// ── P6b 节点操作条——text 块带 msgIndex 渲染两按钮（回滚/分支）；无 msgIndex 不渲染 ──
test('P6b text 块带 msgIndex 渲染节点操作条', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '回复内容' }, origin: { msgIndex: 3 } });
  const bubble = bubbles()[0];
  const bar = bubble.querySelector('.node-actions');
  expect(bar).not.toBeNull();
  expect(bar.querySelectorAll('.node-btn').length).toBe(2);
  expect(bubble.querySelector('.node-btn-rollback').title).toContain('从此处继续对话');
  expect(bubble.querySelector('.node-btn-fork').title).toContain('新建独立 Cat');
});

test('P6b text 块无 msgIndex（工具轮 seal/旧数据）不渲染操作条', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '工具轮文本' } });
  expect(bubbles()[0].querySelector('.node-actions')).toBeNull();
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '负索引' }, origin: { msgIndex: -1 } });
  expect(bubbles()[1].querySelector('.node-actions')).toBeNull();
});


// ── A85 think 两态（2026-09-23 规格变更：流式态 live-only / 完成态落盘；展开折叠 → 高度档）──
test('A85 思考整块——流式块销毁、新建完成块（同屏只剩一个）', () => {
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考' } });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '最终思考' } });
  expect(window.viewContainers['live:11']).toBeUndefined();
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(0);
  const box = chatMsgs.querySelector('.chat-think.done');
  expect(box).not.toBeNull();
  expect(box.querySelector('.ct-full').textContent).toBe('最终思考');
  expect(bubbles().length).toBe(1);
});

test('A85 流式头行——字符/行数实时刷新；完成块取流式终值', () => {
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考' } });
  let head = chatMsgs.querySelector('.chat-think.stream .ct-head');
  expect(head.textContent).toContain('Think · 2 字符 · 1 行');
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '中\n次行' } });
  head = chatMsgs.querySelector('.chat-think.stream .ct-head');
  expect(head.textContent).toContain('6 字符 · 2 行');
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '思考中\n次行' } });
  const done = chatMsgs.querySelector('.chat-think.done .ct-head');
  expect(done.textContent).toContain('Think · 6 字符 · 2 行');
  expect(done.textContent).toContain('未统计');
});

test('A85 压缩档——完成块推入即压缩态（缩略摘要与旧折叠摘要同形），点击切展开档', () => {
  window.chatOnView({
    op: 'persist.append', renderType: 'reason',
    payload: { content: '第一行开始\n第二行\n第三行\n第四行\n最后一行结束' }
  });
  const box = chatMsgs.querySelector('.chat-think.done');
  // 默认压缩档——缩略摘要（首行 + 省略行/字符计数 + 末行）
  expect(box.classList.contains('full')).toBe(false);
  expect(box.querySelector('.ct-peek').textContent).toBe('第一行开始（3行11字符已省略显示）最后一行结束');
  expect(box.querySelector('.ct-full').textContent).toBe('第一行开始\n第二行\n第三行\n第四行\n最后一行结束');
  // 点击 → 展开档；再点 → 回压缩档
  box.querySelector('.ct-body').dispatchEvent(new Event('click', { bubbles: true }));
  expect(box.classList.contains('full')).toBe(true);
  box.querySelector('.ct-body').dispatchEvent(new Event('click', { bubbles: true }));
  expect(box.classList.contains('full')).toBe(false);
});

test('A85 压缩档——少于 3 行不压缩（原文直显，不回退省略）', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '第一行\n第二行' } });
  const box = chatMsgs.querySelector('.chat-think.done');
  expect(box.querySelector('.ct-peek').textContent).toBe('第一行\n第二行');
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '单行思考' } });
  const box2 = chatMsgs.querySelectorAll('.chat-think.done')[1];
  expect(box2.querySelector('.ct-peek').textContent).toBe('单行思考');
});

test('A85 多轮思考——各自成完成块（默认压缩档，不随回复变动）', () => {
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '一轮思考' } });
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '一轮思考' } });
  window.chatOnView({ op: 'live.add', key: 'tool:30', renderType: 'toolcard', payload: { name: 'time', arguments: '{}', result: 'R' } });
  window.chatOnView({ op: 'live.add', key: 'live:12', renderType: 'stream', payload: { kind: 'reasoning', text: '二轮思考' } });
  window.chatOnView({ op: 'live.remove', key: 'live:12' });
  window.chatOnView({ op: 'persist.append', renderType: 'reason', payload: { content: '二轮思考' } });
  window.chatOnView({ op: 'persist.append', renderType: 'text', payload: { content: '最终回复' }, origin: { msgIndex: 3 } });
  const boxes = chatMsgs.querySelectorAll('.chat-think.done');
  expect(boxes.length).toBe(2);
  expect(boxes[0].classList.contains('full')).toBe(false);
  expect(boxes[1].classList.contains('full')).toBe(false);
});

test('A85 历史重建——完成块默认压缩档 + 无时长来源标识「未统计」', () => {
  window.chatRenderHistory({
    version: 1, sessionId: 's-open', count: 2,
    blocks: [
      { key: 'k1', id: '1', renderType: 'reason', payload: { content: '中间思考' } },
      { key: 'k2', id: '2', renderType: 'text', payload: { content: '工具轮文本' }, origin: { msgIndex: -1 } },
      { key: 'k3', id: '3', renderType: 'reason', payload: { content: '最终思考' } },
      { key: 'k4', id: '4', renderType: 'text', payload: { content: '最终回复' }, origin: { msgIndex: 3 } }
    ]
  });
  const boxes = chatMsgs.querySelectorAll('.chat-think.done');
  expect(boxes.length).toBe(2);
  expect(boxes[0].classList.contains('full')).toBe(false);
  expect(boxes[0].querySelector('.ct-head').textContent).toBe('Think · 4 字符 · 1 行 · 未统计');
});

// ── A95 live think 块生命周期归前端（2026-09-28）——态驱动销毁，与后续接什么无关；后端零改动 ──

test('A158 态推送不销毁流式块——移除只认后端 live.remove', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '半截思考' } });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  // 态变化不驱动销毁——前端零自判（流式区忠实镜像后端）
  window.chatOnSessionState({ sessionId: 's1', runState: 'idle', runMs: {}, requests: 1 });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  // 后端清空流式区 → live.remove → 前端删块
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(0);
  expect(chatMsgs.querySelectorAll('.chat-think.done').length).toBe(0);
  expect(window.viewContainers['live:11']).toBeUndefined();
});

test('A158 续传切态——旧请求流式块由后端移除，续传请求另起新块', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '无效请求的思考' } });
  // 后端撤销无效请求的流式块（态切换不驱动前端销毁）
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  window.chatOnSessionState({ sessionId: 's1', runState: 'link', runMs: {}, requests: 2 });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(0);
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 2 });
  window.chatOnView({ op: 'live.add', key: 'live:31', renderType: 'stream', payload: { kind: 'reasoning', text: '续传思考' } });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  expect(window.chatThinkBodyText(window.viewContainers['live:31'].body)).toBe('续传思考');
});

test('A158 态推送全程不影响流式块（同态重推 / 切换 / 刷新重连）', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考中' } });
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  // 刷新重连——DOM/容器清空后新块照常建；态推送不参与销毁
  chatMsgs.textContent = '';
  window.viewContainers = {};
  window.chatPhaseReset();
  window.chatOnView({ op: 'live.add', key: 'live:12', renderType: 'stream', payload: { kind: 'reasoning', text: '重连思考' } });
  window.chatOnSessionState({ sessionId: 's1', runState: 'reply', runMs: {}, requests: 1 });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
});

test('A158 error 持久块不影响流式区——流式块由后端 live.remove 退场', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考中' } });
  window.chatOnView({ op: 'persist.append', renderType: 'error', payload: { text: 'ERR|TEST|模拟失败' } });
  // 持久错误块只新增气泡；流式块仍在（前端零自判）
  expect(chatMsgs.querySelectorAll('.chat-bubble.error').length).toBe(1);
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  // 后端清空流式区 → live.remove
  window.chatOnView({ op: 'live.remove', key: 'live:11' });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(0);
});

// ── 展开态整块点击收起（2026-09-18）——原生 details 仅折叠头可点；展开后整块任意位置点击即收起 ──
test('展开态整块点击收起——工具卡内容区点击即折叠（折叠态不误触）', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:40', renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-09-18 00:00:00' }
  });
  const card = chatMsgs.querySelector('.chat-tool');
  card.open = true;
  card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
  // 折叠态再点内容区——保持在折叠（无异常、不反向展开）
  card.querySelector('.tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});

test('A85 高度档——拖选（死区）不切换，快速点击切换（think 块）', () => {
  window.chatOnView({
    op: 'persist.append', renderType: 'reason',
    payload: { content: '第一行\n第二行\n第三行' }
  });
  const box = chatMsgs.querySelector('.chat-think.done');
  const body = box.querySelector('.ct-body');
  const M = document.defaultView.MouseEvent;
  expect(box.classList.contains('full')).toBe(false);
  // 拖选——位移 60px 超限 → 不切换
  body.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 0, clientY: 0 }));
  body.dispatchEvent(new M('click', { bubbles: true, clientX: 60, clientY: 0 }));
  expect(box.classList.contains('full')).toBe(false);
  // 普通点击——位移 2px 在限内 → 切全文档
  body.dispatchEvent(new M('mousedown', { bubbles: true, clientX: 0, clientY: 0 }));
  body.dispatchEvent(new M('click', { bubbles: true, clientX: 2, clientY: 2 }));
  expect(box.classList.contains('full')).toBe(true);
});

test('展开态整块点击收起——拖选文本（按下时已有选区）不收起，选区清空后恢复收起', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:42', renderType: 'toolcard',
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
    op: 'live.add', key: 'tool:43', renderType: 'toolcard',
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
    op: 'live.add', key: 'tool:44', renderType: 'toolcard',
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
    op: 'live.add', key: 'tool:30', renderType: 'toolcard',
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
    op: 'live.add', key: 'tool:30', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'OK', toolIndex: 1, toolTotal: 4 },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[0].textContent).toBe('[📖 1/4] 读取 (未指定) · 1 行 2 字符');
  // 单次（无 toolTotal 字段——旧数据兼容）——同样用类型图标（text-read → 📖）
  window.chatOnView({
    op: 'live.add', key: 'tool:31', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'OK' },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[1].textContent).toBe('📖 读取 (未指定) · 1 行 2 字符');
  // 错误并发——[⚠️ n/m] 前缀
  window.chatOnView({
    op: 'live.add', key: 'tool:32', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{}', result: 'ERR|X', toolIndex: 2, toolTotal: 4 },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelectorAll('.chat-tool .tn')[2].textContent).toBe('[⚠️ 2/4] 读取 (未指定) · 1 行 5 字符');
});

// ── powershell 命令解读——折叠行由解读承担（PS 双线不配 headline）+ 展开区意图块（chat-cmd.js 接入）──
test('toolcard powershell——命令解读承担折叠行 + 展开区意图块', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:33', renderType: 'toolcard',
    payload: {
      name: 'powershell',
      arguments: JSON.stringify({ command: 'dotnet build CatHome4.sln; git status' }),
      result: '{"exit":0}'
    },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  // 折叠行——由命令解读产出 + PS 版本标签（双线区分，2026-09-18）
  expect(card.querySelector('.tn').textContent).toBe('💻 PS 5.1 dotnet build · 编译 C# 项目 「CatHome4.sln」 等 2 段');
  // 展开区首块——逐段意图（含指令类标识 tag）
  const intent = card.querySelector('.cmd-intent');
  expect(intent).not.toBeNull();
  expect(intent.textContent).toContain('1. dotnet build · 编译 C# 项目 「CatHome4.sln」');
  expect(intent.textContent).toContain('2. git status · 查看仓库状态');
  // 原文块保留（意图块之后）
  expect(card.querySelector('.ta').textContent).toContain('dotnet build');
});

// ── 非 powershell 工具——不进解读（折叠行由覆盖表 headline 产出）──
test('toolcard 非 powershell——不插命令意图块', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:34', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{"path":"a.txt"}', result: 'OK' },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 a.txt · 1 行 2 字符');
  expect(card.querySelector('.cmd-intent')).toBeNull();
});

// ── 结果规模标注——消耗可见（大结果标字符数 / 截断标上限 + 警示块 / 小结果不标）──
test('toolcard 大结果——折叠行标注字符数', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:40', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: '{"path":"big.txt"}', result: 'x'.repeat(2000) },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 big.txt · 1 行 2.00k 字符');
  expect(card.querySelector('.ta.warn')).toBeNull();
});

test('toolcard 小结果——不标注（避免噪音）', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:41', renderType: 'toolcard',
    payload: { name: 'time', arguments: '{}', result: '2026-09-14 17:00:00' },
    replaceSeq: -1
  });
  expect(chatMsgs.querySelector('.chat-tool .tn').textContent).toBe('🕒 时间  · 1 行 19 字符');
});

test('废弃块——timeback 回收归档：折叠气泡 + 计数 + 全文（历史重建）', () => {
  window.chatRenderHistory({
    sessionId: 's1',
    ctxCount: 1,
    blocks: [
      { renderType: 'void', msgIndex: -1, payload: { count: 4, text: '【工具 · random】\n参数：{}\n结果：7' } }
    ]
  });
  const card = chatMsgs.querySelector('.chat-void');
  expect(card).not.toBe(null);
  expect(card.querySelector('summary').textContent).toContain('已废弃 · 4 条');
  // 规模统计（2026-09-29 莎）——折叠行带「N 行 M 字符」
  expect(card.querySelector('summary').textContent).toContain('3 行');
  expect(card.querySelector('summary').textContent).toContain('字符');
  expect(card.querySelector('.chat-void-body').textContent).toContain('random');
  // 默认折叠——只留档，不抢注意力（与活块区分）
  expect(card.open).toBe(false);
  // 独立块型：不进对话流（无 user/assistant 气泡样式）
  expect(chatMsgs.querySelector('.chat-bubble.void')).not.toBe(null);
});

test('toolcard ps 结果截断——折叠行上限标注 + 展开区警示块', () => {
  const psResult = JSON.stringify({ exit: 0, stdout: 'y'.repeat(16384), stderr: '', truncated: true, timeout: false });
  window.chatOnView({
    op: 'live.add', key: 'tool:42', renderType: 'toolcard',
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
    op: 'live.add', key: 'tool:43', renderType: 'toolcard',
    payload: { name: 'powershell', arguments: JSON.stringify({ command: 'Get-Date' }), result: psResult },
    replaceSeq: -1
  });
  const card = chatMsgs.querySelector('.chat-tool');
  expect(card.querySelector('.tn').textContent).toContain('· 1.50k 字符');
  expect(card.querySelector('.ta.warn')).toBeNull();
});

// ── 全局态驱动（2026-09-22 收口唯一化）——流式容器标记随后端运行态切换，不再由各 view 路径自判 ──
test('全局态驱动——text 流式容器标记随态切换（reply 挂 / 离开 reply 撤）', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'reply', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '中间文本' } });
  const tb = bubbles()[0];
  expect(tb.classList.contains('streaming')).toBe(true);
  // 态离开 reply（工具决策流开始）——标记撤除；容器保留（仍须承接整块替换）
  window.chatOnSessionState({ sessionId: 's1', runState: 'tool', runMs: {}, requests: 2 });
  expect(tb.classList.contains('streaming')).toBe(false);
  expect(window.viewContainers['live:10']).not.toBeUndefined();
});

// ── 工具卡两段式（2026-09-16）——先行"进行中"卡 + 完成/中断原位替换 ──
test('toolcard 两段式——先行卡（无 result）显示 ⏳ 处理中 + pending 标记（态驱动）', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'run', runMs: {}, requests: 1 });
  window.chatOnView({
    op: 'live.add', key: 'tool:50', renderType: 'toolcard',
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
  expect(window.viewContainers['tool:50']).not.toBeUndefined();
});

test('A158 toolcard 两段式——同块键先 add 后 update（气泡不新增，pending 标记移除）', () => {
  window.chatOnView({
    op: 'live.add', key: 'tool:50', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }), toolIndex: 1, toolTotal: 1 }
  });
  window.chatOnView({
    op: 'live.update', key: 'tool:50', renderType: 'toolcard',
    payload: { name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }), result: '文件内容', toolIndex: 1, toolTotal: 1 }
  });
  expect(bubbles().length).toBe(1);
  const tb = bubbles()[0];
  expect(tb.classList.contains('pending')).toBe(false);
  const card = tb.querySelector('.chat-tool');
  expect(card.open).toBe(false);
  expect(card.querySelector('.tr').textContent).toBe('文件内容');
  expect(window.viewContainers['tool:50']).not.toBeUndefined();
});

// ── 新思考流式块创建——残留文本流式容器闪烁标记移除（多轮工具循环残留兜底）──
test('新 reasoning 容器创建——残留 text 流式容器 streaming 移除', () => {
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '前轮文本' } });
  const tb = bubbles()[0];
  window.chatOnView({ op: 'live.add', key: 'live:21', renderType: 'stream', payload: { kind: 'reasoning', text: '新一轮思考' } });
  expect(tb.classList.contains('streaming')).toBe(false);
  // 同 seq 续流不清理（think 流式块本身不受影响）
  expect(window.viewContainers['live:21'].type).toBe('thinkstream');
});

// ── control usage ──
test('view control usage 不再渲染 Token（状态条 Token 段已退役——2026-09-22）', () => {
  window.chatOnView({ op: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } } });
  expect(chatStatus.textContent).not.toContain('↑');
  expect(chatStatus.textContent).not.toContain('cache');
});

// ── error 视图（A55——独立 renderType：恒定独立错误气泡；已有容器只 seal 不追加）──
test('view error 有流式容器——seal 已有容器 + 独立错误气泡 + 恢复 idle', () => {
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'text', text: '部分' } });
  const tb = bubbles()[0];
  window.chatOnView({ op: 'persist.append', renderType: 'error', payload: { type: 'error', text: 'LLM 挂了' } });
  expect(tb.textContent).toBe('部分');
  expect(tb.classList.contains('streaming')).toBe(false);
  expect(tb.classList.contains('error')).toBe(false);
  expect(bubbles().length).toBe(2);
  expect(bubbles()[1].textContent).toContain('LLM 挂了');
  expect(bubbles()[1].classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
  // A158——流式容器由后端 live.remove 清（error 持久块不触碰流式区）
  expect(Object.keys(window.viewContainers).length).toBe(1);
});

// ── A55 吞错修复：无流式容器时错误必须可见（新建错误气泡）──
test('view error 无容器——新建错误气泡', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'error', payload: { type: 'error', text: '连接超时' } });
  expect(bubbles().length).toBe(1);
  expect(bubbles()[0].textContent).toContain('连接超时');
  expect(bubbles()[0].classList.contains('error')).toBe(true);
  expect(window.chatState).toBe('idle');
});

// ── control chatdone 终态 ──
test('view control chatdone seal 流式容器 + 恢复 idle', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'reply', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '流式' } });
  const bubble = bubbles()[0];
  expect(bubble.classList.contains('streaming')).toBe(true);
  window.chatOnView({ op: 'control', payload: { type: 'chatdone', count: 3 } });
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
  expect(chatMsgs.querySelector('.chat-think.done .ct-full').textContent).toBe('思考');
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
  // A114 平均首 token 延迟——link 320ms ÷ 5 次 = 0.06 s/use
  expect(bar.textContent).toContain('🔄 Api 5（0.06 s /use）');
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
test('2026-09-22 底部行——Token 统计整体退役（会话级与本轮均不渲染）', () => {
  window.chatOnView({
    op: 'control',
    payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30, sessionPrompt: 5000, sessionCompletion: 1200, sessionCacheHit: 800 } },
    replaceSeq: -1
  });
  const bar = chatStatus;
  expect(bar.textContent).not.toContain('↑');
  expect(bar.textContent).not.toContain('↓');
  expect(bar.textContent).not.toContain('cache');
  expect(bar.textContent).not.toContain('miss');
  expect(bar.textContent).not.toContain('会话 ↑');
});

test('A61 轮结束——底部态与统计一并消失（空行）', () => {
  window.chatOnSessionState({ sessionId: 'majordomo', runState: 'run', runMs: { link: 320, think: 28500, run: 11449 }, requests: 5 });
  window.chatOnView({ op: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30 } } });
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

// ── E 系列：Token 统计（F2.4 迁移）——2026-09-22 用例随状态条 Token 段退役移除（回归防护见上方 usage 用例）──

// ── 发送流程：user 事件 FIFO 移除插话队列（F2.4 迁移）──
test('user 事件渲染用户气泡（含插话队列 FIFO 移除）', () => {
  window.chatPending = ['测试消息'];
  window.chatRenderPending();
  window.chatOnView({ op: 'persist.append', renderType: 'user', payload: { content: '测试消息', source: 'user' } });
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
  window.chatOnView({ op: 'live.add', key: 'live:10', renderType: 'stream', payload: { kind: 'text', text: '部分' } });
  window.chatFail('发送失败');
  expect(window.chatState).toBe('idle');
  expect(Object.keys(window.viewContainers).length).toBe(0);
  const errBubbles = chatMsgs.querySelectorAll('.chat-bubble.error');
  expect(errBubbles.length).toBeGreaterThan(0);
  expect(errBubbles[errBubbles.length - 1].textContent).toBe('发送失败');
});

// ── S2 §8.4 retry 独立视图条目 ──
test('view retry 新建重试气泡（⟳ 重试中 N/3）', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: 'ERR|TRANSPORT|模拟抖动' } });
  expect(rows().length).toBe(1);
  const rb = bubbles()[0];
  expect(rb.classList.contains('retry')).toBe(true);
  expect(rb.textContent).toContain('⟳ 重试中 1/3');
  expect(rb.textContent).toContain('模拟抖动');
});

test('A158 retry 只增不改——每次重试独立成气泡（不再原位更新）', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: '首次失败' } });
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'retrying', attempt: '2', max: '3', text: '再次失败' } });
  expect(rows().length).toBe(2);
  expect(bubbles()[1].textContent).toContain('⟳ 重试中 2/3');
});

test('A158 retry 成功态独立成块（A86——保留报错原文 + 追加已恢复）', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'retrying', attempt: '1', max: '3', text: '失败' } });
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'resolved', attempt: '1', max: '3', text: '失败' } });
  expect(rows().length).toBe(2);
  expect(bubbles()[1].textContent).toContain('⟳ 重试中 1/3 · 失败');
  expect(bubbles()[1].textContent).toContain('✓ 已恢复');
  expect(bubbles()[1].classList.contains('resolved')).toBe(true);
});

test('view retry 历史重建——retry 块渲染（chatRenderHistory）', () => {
  window.chatRenderHistory({
    blocks: [
      { renderType: 'retry', payload: { state: 'retrying', attempt: '2', max: '3', text: '历史失败' } },
      { renderType: 'retry', payload: { state: 'resolved', attempt: '2', max: '3', text: '历史失败' } }
    ],
    sessionId: 's1',
    count: 2
  });
  expect(rows().length).toBe(2);
  expect(bubbles()[0].textContent).toContain('⟳ 重试中 2/3');
  expect(bubbles()[1].textContent).toContain('⟳ 重试中 2/3 · 历史失败');
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
  window.chatOnView({ op: 'persist.append', renderType: 'user', payload: { content: '旧气泡', source: 'user' } });
  expect(bubbles().length).toBe(1);
  // fetch mock——history 返回空（新会话无前文）
  const origFetch = window.fetch;
  window.fetch = function (url) {
    if (String(url).indexOf('/api/v1/history') >= 0) {
      return Promise.resolve({ json: function () { return Promise.resolve({ sessionId: 's2', count: 0, blocks: [], stats: { prompt: 0, cacheHit: 0, completion: 0 } }); } });
    }
    return Promise.resolve({ json: function () { return Promise.resolve({ ok: true }); } });
  };
  window.chatOnView({ op: 'control', payload: { type: 'session_reset' } });
  await new Promise(function (r) { setTimeout(r, 20); });
  window.fetch = origFetch;
  expect(window.chatPendingReset).toBe(false);
  expect(bubbles().length).toBe(0);   // 旧气泡已清
  expect(chatInfo.textContent).toContain('前文 0 条');
});

// ── 会话终态统计（问题三——chatdone 带真实 usage；顶部栏只显示前文长度=context 单次值）──
test('control chatdone 显示前文真实 usage（前文长度 context）', () => {
  window.chatOnView({ op: 'live.add', key: 'live:1', renderType: 'stream', payload: { kind: 'text', text: '回复' } });
  window.chatOnView({ op: 'control', payload: { type: 'chatdone', count: 5, stats: { prompt: 120, cacheHit: 40, completion: 30, context: 150 } } });
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
  window.chatOnView({ op: 'control', payload: { type: 'chatdone', count: 1 } });
  await new Promise(function (r) { setTimeout(r, 20); });
  window.fetch = origFetch;
  expect(window.chatPendingReset).toBe(false);
  expect(chatInfo.textContent).toContain('前文 0 条');
});

// ── view/control note 分支（宿主经 PushView 推送 Note 状态——前端转交 noteOnEvent 重绘）──
test('control note 分支——view 载荷更新 Note 面板（不自动展开）', () => {
  window.noteModalOpen = false;
  document.getElementById('notePopover').style.display = 'none';
  window.chatOnView({ op: 'control', payload: { type: 'note', state: { tasks: ['前端任务'], current: 0, done: 0 } } });
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
  window.chatOnView({ op: 'control', payload: { type: 'chatdone', count: 48, stats: { prompt: 435652, cacheHit: 363008, completion: 12158, context: 151234 } } });
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
  window.chatOnView({ op: 'persist.append', renderType: 'roundsum', payload: { type: 'roundsum', data: { prompt: 769770, completion: 3550, cacheHit: 765700, miss: 4070, toolCount: 6, requests: 5, elapsedMs: 76200, phases: { wait: 480, link: 200, think: 28500, tool: 773, run: 45100, reply: 2400 } } } });
  const rs = chatMsgs.querySelector('.chat-bubble.roundsum');
  expect(rs).not.toBeNull();
  expect(rs.textContent).toContain('Round');
  expect(rs.textContent).toContain('↑769.77k');
  expect(rs.textContent).toContain('↓3.55k');
  expect(rs.textContent).toContain('cache 765.70k');
  expect(rs.textContent).toContain('miss 4.07k');
  expect(rs.textContent).toContain('🎯99.5%');
  expect(rs.textContent).toContain('🔧 Tool 6 · 🔄 Api 5');
  // A114 平均首 token 延迟——link 200ms ÷ 5 次 = 0.04 s/use
  expect(rs.textContent).toContain('🔄 Api 5（0.04 s /use）');
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

// ── A114 旧载荷兼容：无 link 时不加平均后缀（历史块零迁移）──
test('A114 旧载荷无 link——请求段不加平均后缀', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'roundsum', payload: { type: 'roundsum', data: { prompt: 100, completion: 10, cacheHit: 0, miss: 100, toolCount: 0, requests: 3, elapsedMs: 5000, phases: { think: 100 } } } });
  const all = chatMsgs.querySelectorAll('.chat-bubble.roundsum');
  const last = all[all.length - 1];
  expect(last.textContent).toContain('🔄 Api 3');
  expect(last.textContent).not.toContain('s /use');
});

// ── A115 前文条数与 tokens 同节奏（usage 事件双更新）──
test('A115 usage 事件同步刷新前文条数与 tokens（不等轮结束）', () => {
  window.chatRenderHistory({ version: 1, sessionId: 's-a115', ctxCount: 12, count: 12, blocks: [], stats: { context: 1000 } });
  expect(chatInfo.textContent).toContain('前文 12 条');
  window.chatOnView({ op: 'control', payload: { type: 'usage', data: { prompt: 100, completion: 20, cacheHit: 30, context: 1200, count: 14 } } });
  expect(chatInfo.textContent).toContain('前文 14 条');
  expect(chatInfo.textContent).toContain('前文 1.20k tokens');
});

// ── A85 头行统计口径（原「折叠摘要」退役——展开/折叠已由高度档取代）──
test('A85 头行统计——速率取整显示；已输出 <1s 不显示速率（分母无意义）', () => {
  expect(window.chatThinkHeadText({ chars: 1200, lines: 34, ms: 12000 })).toBe('Think · 1.20k 字符 · 34 行 · 12.0s · 100 字符/s');
  expect(window.chatThinkHeadText({ chars: 300, lines: 5, ms: 900 })).toBe('Think · 300 字符 · 5 行 · 0.9s');
});

// ── A85 无统计来源（历史重建 / 刷新重建）——时间与速率位跳过并标识「未统计」──
test('A85 头行统计——无时长来源跳过时间与速率并标识「未统计」', () => {
  expect(window.chatThinkHeadText({ chars: 12, lines: 2, ms: null })).toBe('Think · 12 字符 · 2 行 · 未统计');
  expect(window.chatThinkHeadText({ chars: 0, lines: 0, ms: null })).toBe('Think · 0 字符 · 0 行 · 未统计');
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

// ── A78/A85 全局态唯一出入口——think 流式块标记（live）随 think 态挂载/撤除 ──
test('A78/A85 态驱动——think 流式块标记随 think 态切换（think 挂 live / 离开 think 撤）', () => {
  // 思考流式——态 think 下挂标记
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:11', renderType: 'stream', payload: { kind: 'reasoning', text: '思考中' } });
  const rb = chatMsgs.querySelector('.chat-think.stream').closest('.chat-bubble');
  expect(rb.classList.contains('live')).toBe(true);
  // 离开 think（回复流式开始）——标记撤除；流式块由后端 live.remove 驱动销毁（A158：前端零自判）
  window.chatOnSessionState({ sessionId: 's1', runState: 'reply', runMs: {}, requests: 1 });
  expect(chatMsgs.querySelectorAll('.chat-think.stream').length).toBe(1);
  expect(window.viewContainers['live:11']).not.toBeUndefined();
});

// ── A78 容器创建时按当前态初始化标记（态未变化时的补挂路径）──
test('A78 态驱动——容器创建即按当前态挂标记（多容器各自归类）', () => {
  window.chatOnSessionState({ sessionId: 's1', runState: 'think', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:12', renderType: 'stream', payload: { kind: 'reasoning', text: '二轮思考' } });
  const rb2 = chatMsgs.querySelectorAll('.chat-think.stream')[0].closest('.chat-bubble');
  expect(rb2.classList.contains('live')).toBe(true);
  // 态切到 reply——思考块撤、后续回复容器挂
  window.chatOnSessionState({ sessionId: 's1', runState: 'reply', runMs: {}, requests: 1 });
  window.chatOnView({ op: 'live.add', key: 'live:20', renderType: 'stream', payload: { kind: 'text', text: '回复' } });
  const tb = chatMsgs.querySelector('.chat-bubble.streaming');
  expect(tb).not.toBeNull();
  expect(tb.textContent).toBe('回复');
  expect(rb2.classList.contains('live')).toBe(false);
});

// ═══════════════════════════════════════════
// A77 静默兜底（C 方案 · 2026-09-22）——只作明确展示：不改在途块状态 / 不清容器 / 不假收口
// ═══════════════════════════════════════════

test('A77 静默兜底——120s 静默贴提示，且不动在途容器状态（pending 保留 · 容器不清 · 仍 sending）', () => {
  vi.useFakeTimers();
  try {
    window.chatState = 'sending';
    // 先行工具卡（pending——蓝色呼吸态来源；标记由全局态驱动）
    window.chatOnSessionState({ sessionId: 's1', runState: 'run', runMs: {}, requests: 1 });
    window.chatOnLiveToolCard('tool:1', { name: 'mau-setup', arguments: '{}', toolIndex: 1, toolTotal: 1 });
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
  window.chatOnView({ renderType: 'text', seq: -1, payload: { content: '已恢复', msgIndex: -1 } });
  expect(chatMsgs.querySelector('.chat-bubble.stall')).toBeNull();
});

// ═══════════════════════════════════════════
// A84 工具卡「运行中」已运行时长（纯前端自算 · 1 秒粒度 · 2026-09-23）
// ═══════════════════════════════════════════

// 建先行卡（无 result → pending）并返回占位元素——exec 骨架与通用输出段共用同一占位出口
function pendingHold(seq, name) {
  window.chatOnLiveToolCard('tool:' + seq, { name: name, arguments: '{}', toolIndex: 1, toolTotal: 1 });
  return chatMsgs.querySelector('.chat-tool .' + window.CHAT_PENDING_HOLD_CLS);
}

test('A84 占位行带已运行时长——exec 骨架与通用输出段同一出口（初始 0s）', () => {
  const exec = pendingHold(1, 'mau-setup');
  expect(exec).toBeTruthy();
  expect(exec.className).toContain('ta');
  expect(exec.textContent).toBe('⏳ 处理中…（已运行 0s）');
  const plain = pendingHold(2, 'text-read');
  expect(plain.textContent).toBe('⏳ 处理中…（已运行 0s）');
});

test('A84 粒度 1 秒——整秒向下取整（3500ms → 3s · 12000ms → 12s）', () => {
  vi.useFakeTimers();
  try {
    const hold = pendingHold(1, 'mau-setup');
    hold.setAttribute('data-start', String(Date.now() - 3500));
    window.chatLiveTick();
    expect(hold.textContent).toBe('⏳ 处理中…（已运行 3s）');
    hold.setAttribute('data-start', String(Date.now() - 12000));
    window.chatLiveTick();
    expect(hold.textContent).toBe('⏳ 处理中…（已运行 12s）');
  } finally {
    vi.useRealTimers();
  }
});

test('A84 1 秒表驱动——建卡即开表，每秒 tick 一次', () => {
  vi.useFakeTimers();
  try {
    const spy = vi.spyOn(window, 'chatLiveTick');
    pendingHold(1, 'mau-setup');
    expect(window.chatLiveTimer).toBeTruthy();
    vi.advanceTimersByTime(3000);
    expect(spy).toHaveBeenCalledTimes(3);
    spy.mockRestore();
  } finally {
    vi.useRealTimers();
  }
});

test('A158 完成原位替换——占位随换卡消失；卡留流式区（表继续跑，待持久块换手）', () => {
  pendingHold(1, 'mau-setup');
  expect(window.chatLiveTimer).toBeTruthy();
  window.chatOnLiveToolCard('tool:1', { name: 'mau-setup', arguments: '{}', result: 'OK', toolIndex: 1, toolTotal: 1 });
  expect(chatMsgs.querySelector('.chat-tool .' + window.CHAT_PENDING_HOLD_CLS)).toBeNull();
  // 终态卡不落盘、仍在流式区等 persist.append 换手——计时表不随换卡停
  expect(window.chatLiveTimer).not.toBeNull();
});

test('A84 终态卡直达（无先行卡）——不建占位、不开表', () => {
  window.chatOnLiveToolCard('tool:1', { name: 'mau-setup', arguments: '{}', result: 'OK', toolIndex: 1, toolTotal: 1 });
  expect(chatMsgs.querySelector('.chat-tool .' + window.CHAT_PENDING_HOLD_CLS)).toBeNull();
  expect(window.chatLiveTimer).toBeNull();
});

// ── A94 重试语义流补全（failed 终态 + 原文不丢）；A158 只增不改——每次重试独立成块 ──
test('A158 retry failed 独立成块——保留报错原文 + ⚠ 重试失败 + 终态类', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'retrying', attempt: '3', max: '3', text: 'ERR|TRANSPORT|连接失败' } });
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'failed', attempt: '3', max: '3', text: 'ERR|TRANSPORT|连接失败' } });
  expect(rows().length).toBe(2);
  expect(bubbles()[1].textContent).toContain('⚠ 重试失败 3/3');
  expect(bubbles()[1].textContent).toContain('连接失败');
  expect(bubbles()[1].classList.contains('failed')).toBe(true);
  expect(bubbles()[1].classList.contains('resolved')).toBe(false);
});

test('A158 retry 终态各自成块——resolved 与 failed 状态类互不污染', () => {
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'resolved', attempt: '1', max: '3', text: 'x' } });
  expect(bubbles()[0].classList.contains('resolved')).toBe(true);
  window.chatOnView({ op: 'persist.append', renderType: 'retry', payload: { state: 'failed', attempt: '1', max: '3', text: 'x' } });
  expect(bubbles()[1].classList.contains('failed')).toBe(true);
  expect(bubbles()[1].classList.contains('resolved')).toBe(false);
});

test('view retry 历史重建——failed 块渲染（原文 + 失败标注 + 终态类）', () => {
  window.chatRenderHistory({
    blocks: [
      { renderType: 'retry', payload: { state: 'failed', attempt: '3', max: '3', text: '历史失败原文' } }
    ],
    sessionId: 's2',
    count: 1
  });
  expect(rows().length).toBe(1);
  expect(bubbles()[0].textContent).toContain('⚠ 重试失败 3/3 · 历史失败原文');
  expect(bubbles()[0].classList.contains('failed')).toBe(true);
});

// ── 继续指令（cat.continue）——不追加消息：用当前前文再发一次请求；与停止互斥（仅 idle 可用）──
test('chatContinue——投递 cat.continue + 本地转 sending + 清阶段残留', async () => {
  let sent = null;
  globalThis.fetch = async function (url, opts) {
    sent = JSON.parse(opts.body);
    return { json: async function () { return { ok: true }; } };
  };
  window.chatState = 'idle';
  window.chatSetState('idle');
  window.viewContainers = { stale: 1 };
  window.chatContinue();
  expect(sent.text).toBe('cat.continue');
  expect(window.chatState).toBe('sending');
  expect(Object.keys(window.viewContainers).length).toBe(0);
});

test('继续按钮——仅 idle 可用（sending 禁用，与停止互补）', () => {
  const contBtn = document.getElementById('chatContinue');
  const pauseBtn = document.getElementById('chatPause');
  window.chatSetState('idle');
  expect(contBtn.disabled).toBe(false);
  expect(pauseBtn.disabled).toBe(true);
  window.chatSetState('sending');
  expect(contBtn.disabled).toBe(true);
  expect(pauseBtn.disabled).toBe(false);
  window.chatSetState('idle');
});

test('chatContinue——非 idle 不投递（与停止互斥）', () => {
  let called = false;
  globalThis.fetch = async function () { called = true; return { json: async function () { return { ok: true }; } }; };
  window.chatState = 'sending';
  window.chatContinue();
  expect(called).toBe(false);
  window.chatState = 'idle';
});

// ═══════════════════════════════════════════
// 历史窗口分页（2026-10-02）——首屏只拉尾部窗口（最新数据最先出现）+ 上拉补更早的块（保位，不打断阅读）
// ═══════════════════════════════════════════

// fetch 桩——按 match 子串返回固定响应；记录请求 URL（断言请求形态）
function historyFetchStub(routes) {
  const urls = [];
  const orig = window.fetch;
  window.fetch = function (url) {
    urls.push(String(url));
    for (let i = 0; i < routes.length; i++) {
      if (String(url).indexOf(routes[i].match) >= 0) {
        return Promise.resolve({ json: async function () { return routes[i].body; } });
      }
    }
    return Promise.resolve({ json: async function () { return {}; } });
  };
  return { urls: urls, restore: function () { window.fetch = orig; } };
}

function textBlock(n) {
  return { renderType: 'text', payload: { content: '块' + n }, msgIndex: n };
}

// 伪布局——jsdom 无排版：scrollHeight 按子节点数折算，scrollTop/clientHeight 可读写
function fakeScrollMetrics(height, clientH) {
  Object.defineProperty(chatMsgs, 'scrollHeight', { get: function () { return height(); }, configurable: true });
  let top = 0;
  Object.defineProperty(chatMsgs, 'scrollTop', { get: function () { return top; }, set: function (v) { top = v; }, configurable: true });
  Object.defineProperty(chatMsgs, 'clientHeight', { get: function () { return clientH; }, configurable: true });
  return { top: function () { return top; } };
}

test('历史首屏——只拉尾部窗口（max=CHAT_HISTORY_PAGE）并记录窗口起点', async () => {
  window.chatViewStart = -1;
  const stub = historyFetchStub([{ match: '/api/v1/history', body: { sessionId: 's1', count: 300, start: 298, ctxCount: 300, blocks: [textBlock(299), textBlock(300)], stats: { context: 0 } } }]);
  window.chatLoadHistory();
  await new Promise(function (r) { setTimeout(r, 30); });
  stub.restore();
  expect(stub.urls.length).toBe(1);
  expect(stub.urls[0]).toBe('/api/v1/history?max=' + window.CHAT_HISTORY_PAGE);
  expect(window.chatViewStart).toBe(298);
  // 顶部哨兵行——还有更早的块（恒为首子节点）
  const sentinel = chatMsgs.querySelector('.chat-older');
  expect(sentinel).not.toBeNull();
  expect(sentinel.textContent).toContain('上拉加载更早的消息');
  expect(chatMsgs.firstChild).toBe(sentinel);
});

test('历史首屏——窗口已含会话开头（start=0）时哨兵报「已到最早」', async () => {
  window.chatViewStart = -1;
  const stub = historyFetchStub([{ match: '/api/v1/history', body: { sessionId: 's2', count: 2, start: 0, gen: 1, blocks: [textBlock(1), textBlock(2)], stats: { context: 0 } } }]);
  window.chatLoadHistory();
  await new Promise(function (r) { setTimeout(r, 30); });
  stub.restore();
  expect(window.chatViewStart).toBe(0);
  expect(chatMsgs.querySelector('.chat-older').textContent).toContain('已到最早');
});

test('上拉补历史——before=窗口起点 + 更早块插到窗口头部保序 + 阅读位置保位', async () => {
  chatMsgs.textContent = '';
  window.CHAT_SESSION = 's3';
  window.chatViewStart = 298;
  window.chatOlderLoading = false;
  window.chatRenderHistory({ sessionId: 's3', count: 300, start: 298, blocks: [textBlock(299), textBlock(300)], stats: { context: 0 } });
  await new Promise(function (r) { setTimeout(r, 10); });
  // 伪布局——3 个子节点（哨兵 + 2 行）× 100px；clientHeight 0 = 不触发视口填充（单页断言）
  const metrics = fakeScrollMetrics(function () { return chatMsgs.children.length * 100; }, 0);
  chatMsgs.scrollTop = 50;
  const stub = historyFetchStub([{ match: 'before=', body: { sessionId: 's3', count: 300, start: 296, blocks: [textBlock(297), textBlock(298)] } }]);
  window.chatLoadOlder();
  await new Promise(function (r) { setTimeout(r, 30); });
  stub.restore();
  expect(stub.urls[0]).toBe('/api/v1/history?before=298&max=' + window.CHAT_HISTORY_PAGE);
  // 顺序——哨兵 → 297 → 298 → 299 → 300（更早的块插到窗口头部，不是追加到尾部）
  const order = [];
  const all = chatMsgs.querySelectorAll('.chat-bubble');
  for (let i = 0; i < all.length; i++) { order.push(all[i].textContent.replace(/[⟲⧉]/g, '')); }
  expect(order).toEqual(['块297', '块298', '块299', '块300']);
  expect(chatMsgs.firstChild.className).toBe('chat-older');
  // 窗口基线推进 + 阅读位置保位（新增两行 200px 补进 scrollTop）
  expect(window.chatViewStart).toBe(296);
  expect(metrics.top()).toBe(250);
});

test('上拉补历史——已到最早（start=0）不发请求', () => {
  window.chatViewStart = 0;
  window.chatOlderLoading = false;
  const stub = historyFetchStub([]);
  window.chatLoadOlder();
  stub.restore();
  expect(stub.urls.length).toBe(0);
  expect(chatMsgs.querySelector('.chat-older').textContent).toContain('已到最早');
});

test('上拉补历史——会话已切换回落首屏窗口', async () => {
  chatMsgs.textContent = '';
  window.CHAT_SESSION = 's4';
  window.chatViewStart = 10;
  window.chatOlderLoading = false;
  const stub = historyFetchStub([
    { match: 'before=', body: { sessionId: 's9', count: 12, start: 8, blocks: [textBlock(9)] } },
    { match: '/api/v1/history', body: { sessionId: 's9', count: 12, start: 10, blocks: [textBlock(11), textBlock(12)], stats: { context: 0 } } }
  ]);
  window.chatLoadOlder();
  await new Promise(function (r) { setTimeout(r, 30); });
  stub.restore();
  expect(stub.urls[0]).toBe('/api/v1/history?before=10&max=' + window.CHAT_HISTORY_PAGE);
  expect(stub.urls[1]).toBe('/api/v1/history?max=' + window.CHAT_HISTORY_PAGE);
});
