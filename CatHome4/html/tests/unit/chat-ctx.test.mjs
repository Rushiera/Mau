// tests/unit/chat-ctx.test.mjs —— 前文弹层单元测试（js/chat-ctx.js + 状态栏前文三段 chat-core.js chatInfo*）
// 覆盖：弹层四视图渲染（按条 / 按 token 分布 / 关键信息 / 完整前文）· 空态与错误态 · 条目展开折叠 · 状态栏结构化 + patch 重建 + 点击开层
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach, vi } from 'vitest';

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  const dom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  globalThis.window = globalThis;
  globalThis.document = dom.window.document;
  globalThis.Node = dom.window.Node;
  globalThis.HTMLElement = dom.window.HTMLElement;
  const common = await readFile(new URL('../../js/ui-common.js', import.meta.url), 'utf-8');
  const view = await readFile(new URL('../../js/chat-view.js', import.meta.url), 'utf-8');
  const core = await readFile(new URL('../../js/chat-core.js', import.meta.url), 'utf-8');
  const ctx = await readFile(new URL('../../js/chat-ctx.js', import.meta.url), 'utf-8');
  vm.runInThisContext(common, { filename: 'ui-common.js' });
  vm.runInThisContext(view, { filename: 'chat-view.js' });
  vm.runInThisContext(core, { filename: 'chat-core.js' });
  vm.runInThisContext(ctx, { filename: 'chat-ctx.js' });
});

function clickEv() {
  const ev = document.createEvent('Event');
  ev.initEvent('click', true, true);
  return ev;
}

function fakeData() {
  return {
    ok: true, count: 3, start: 1, shown: 3, chars: 300, ctxTokens: 1200,
    items: [
      { i: 1, role: 'system', tool: '', chars: 200, time: 0, truncated: false, preview: '系统提示', content: '系统提示全文' },
      { i: 2, role: 'user', tool: '', chars: 60, time: 0, truncated: false, preview: '用户问题', content: '用户问题全文' },
      { i: 3, role: 'tool', tool: 'text-read', chars: 40, time: 0, truncated: true, preview: '结果', content: '结果全文' }
    ]
  };
}

beforeEach(() => {
  window.chatCtxData = null;
  window.chatKeyData = null;
  window.chatFullData = null;
  window.chatCtxMode = 'list';
  document.getElementById('ctxList').textContent = '';
  document.getElementById('ctxPopover').style.display = 'none';
  document.getElementById('chatInfo').textContent = '';
});

// ── 弹层空态与错误态 ──
test('弹层——无数据时渲染不可读提示', () => {
  window.chatCtxData = null;
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('前文不可读');
});

test('弹层——错误响应原样出声（宿主 error 文本）', () => {
  window.chatCtxData = { ok: false, error: '前文快照失败: boom' };
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('前文快照失败: boom');
});

// ── list 视图 ──
test('list 视图——按条目顺序渲染 role 标签 / 序号 / 字符数 / 截断标记', () => {
  window.chatCtxData = fakeData();
  window.chatCtxMode = 'list';
  window.chatCtxRender();
  const rows = document.querySelectorAll('#ctxList .ctx-item');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('.ctx-role').textContent).toBe('system');
  expect(rows[1].querySelector('.ctx-role').textContent).toBe('user');
  expect(rows[0].querySelector('.ctx-i').textContent).toBe('#1');
  expect(rows[2].querySelector('.ctx-size').textContent).toContain('已截断');
  expect(rows[2].querySelector('.ctx-tool').textContent).toBe('text-read');
  expect(document.getElementById('ctxMeta').textContent).toContain('共 3 条');
  expect(document.getElementById('ctxMeta').textContent).toContain('1.20k tokens');
});

test('list 视图——点击条目头展开全文，再点折叠', () => {
  window.chatCtxData = fakeData();
  window.chatCtxMode = 'list';
  window.chatCtxRender();
  const row = document.querySelectorAll('#ctxList .ctx-item')[1];
  const head = row.querySelector('.ctx-item-head');
  const full = row.querySelector('.ctx-full');
  const body = row.querySelector('.ctx-body');
  expect(full.style.display).toBe('none');
  head.dispatchEvent(clickEv());
  expect(full.style.display).toBe('block');
  expect(body.style.display).toBe('none');
  expect(full.textContent).toBe('用户问题全文');
  head.dispatchEvent(clickEv());
  expect(full.style.display).toBe('none');
  expect(body.style.display).toBe('block');
});

test('list 视图——条目为空时出声（不静默留白）', () => {
  window.chatCtxData = { ok: true, count: 0, chars: 0, ctxTokens: 0, items: [] };
  window.chatCtxMode = 'list';
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('前文为空');
});

// ── tokens 视图 ──
test('tokens 视图——按字符规模降序 + 占比 + 估算口径标注', () => {
  window.chatCtxData = fakeData();
  window.chatCtxMode = 'tokens';
  window.chatCtxRender();
  const rows = document.querySelectorAll('#ctxList .ctx-item');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('.ctx-i').textContent).toBe('#1');
  expect(rows[1].querySelector('.ctx-i').textContent).toBe('#2');
  expect(rows[2].querySelector('.ctx-i').textContent).toBe('#3');
  expect(rows[0].querySelector('.ctx-pct').textContent).toBe('66.7%');
  expect(rows[0].querySelector('.ctx-bar-fill').style.width).toContain('66.6');
  expect(document.getElementById('ctxMeta').textContent).toContain('估算');
  expect(document.getElementById('ctxTitle').textContent).toBe('前文 token 明细');
});

test('视图切换——chatCtxSwitch 置高亮并重渲染', () => {
  window.chatCtxData = fakeData();
  window.chatCtxSwitch('tokens');
  expect(document.getElementById('ctxModeTokens').className).toContain('ctx-mode-on');
  expect(document.getElementById('ctxModeList').className).not.toContain('ctx-mode-on');
  window.chatCtxSwitch('list');
  expect(document.getElementById('ctxModeList').className).toContain('ctx-mode-on');
});

// ── 状态栏前文两段 ──
test('状态栏两段——结构化渲染（条数 / tokens 各一段 + sessionId）', () => {
  window.chatInfoSet('sess-1', 48, 151230);
  expect(document.getElementById('chatCtxCount').textContent).toBe('前文 48 条');
  expect(document.getElementById('chatCtxTokens').textContent).toBe('前文 151.23k tokens');
  expect(document.getElementById('chatInfo').textContent).toContain('sessionId=sess-1');
});

test('状态栏两段——patch 改数值，且被临时提示覆盖后自动重建', () => {
  window.chatInfoSet('s', 10, 0);
  window.chatInfoPatch(12, 2048);
  expect(document.getElementById('chatCtxCount').textContent).toBe('前文 12 条');
  expect(document.getElementById('chatCtxTokens').textContent).toBe('前文 2.05k tokens');
  document.getElementById('chatInfo').textContent = '已停止本轮（前文保留）';
  window.chatInfoPatch(13, undefined);
  expect(document.getElementById('chatCtxCount').textContent).toBe('前文 13 条');
  expect(document.getElementById('chatCtxTokens').textContent).toBe('前文 2.05k tokens');
});

test('状态栏点击条数段——打开弹层且不被外部点击关闭逻辑误关', () => {
  vi.stubGlobal('fetch', () => Promise.resolve({ json: () => Promise.resolve(fakeData()) }));
  window.chatInfoSet('s', 3, 1200);
  document.getElementById('chatCtxCount').dispatchEvent(clickEv());
  expect(document.getElementById('ctxPopover').style.display).toBe('block');
  expect(document.getElementById('ctxTitle').textContent).toBe('前文条目');
  vi.unstubAllGlobals();
});

// ── 点击切换（展开即读取 / 同视图再点收起 / 异视图复用缓存）──
test('点击切换——同视图再点收起，不重复读取', () => {
  let calls = 0;
  vi.stubGlobal('fetch', () => { calls = calls + 1; return Promise.resolve({ json: () => Promise.resolve(fakeData()) }); });
  window.chatInfoSet('s', 3, 1200);
  const c = document.getElementById('chatCtxCount');
  c.dispatchEvent(clickEv());
  expect(document.getElementById('ctxPopover').style.display).toBe('block');
  c.dispatchEvent(clickEv());
  expect(document.getElementById('ctxPopover').style.display).toBe('none');
  expect(calls).toBe(1);
  vi.unstubAllGlobals();
});

test('点击切换——已开时点另一段切视图，复用缓存不重复读取', () => {
  let calls = 0;
  vi.stubGlobal('fetch', () => { calls = calls + 1; return Promise.resolve({ json: () => Promise.resolve(fakeData()) }); });
  window.chatInfoSet('s', 3, 1200);
  document.getElementById('chatCtxCount').dispatchEvent(clickEv());
  window.chatCtxData = fakeData();
  window.chatCtxMode = 'list';
  document.getElementById('chatCtxTokens').dispatchEvent(clickEv());
  expect(document.getElementById('ctxPopover').style.display).toBe('block');
  expect(window.chatCtxMode).toBe('tokens');
  expect(document.getElementById('ctxTitle').textContent).toBe('前文 token 明细');
  expect(calls).toBe(1);
  vi.unstubAllGlobals();
});

// ── key 视图（前文关键信息——本次会话四部分：加载报告 / 用户消息 / 正式回复 / 轮结算） ──
function fakeKeyData() {
  return {
    ok: true, count: 3, start: 1, shown: 3, chars: 260,
    items: [
      { i: 1, role: 'report', time: 0, chars: 120, truncated: false, preview: '注入清单：12 个文件', content: '注入清单：12 个文件（ok 12 / missing 0 / 失败 0）' },
      { i: 2, role: 'user', time: 0, chars: 80, truncated: false, preview: '我想给mau加一些易用性改动', content: '我想给mau加一些易用性改动，上工把' },
      { i: 3, role: 'roundsum', time: 0, chars: 60, truncated: false, preview: 'Token 上 100 / 下 50', content: 'Token 上 100 / 下 50（缓存 10）· 工具 1 次 · 请求 2 次 · 用时 1.2 秒' }
    ]
  };
}

test('key 视图——标题 / meta / 四类条目中文标签', () => {
  window.chatKeyData = fakeKeyData();
  window.chatCtxMode = 'key';
  window.chatCtxRender();
  expect(document.getElementById('ctxTitle').textContent).toBe('前文关键信息');
  expect(document.getElementById('ctxMeta').textContent).toContain('共 3 条');
  const rows = document.querySelectorAll('#ctxList .ctx-item');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('.ctx-role').textContent).toBe('加载报告');
  expect(rows[0].querySelector('.ctx-role').className).toContain('ctx-role-report');
  expect(rows[1].querySelector('.ctx-role').textContent).toBe('用户');
  expect(rows[2].querySelector('.ctx-role').textContent).toBe('轮结算');
});

test('key 视图——条目可展开全文（与前文弹层同款交互）', () => {
  window.chatKeyData = fakeKeyData();
  window.chatCtxMode = 'key';
  window.chatCtxRender();
  const row = document.querySelectorAll('#ctxList .ctx-item')[1];
  const head = row.querySelector('.ctx-item-head');
  const full = row.querySelector('.ctx-full');
  expect(full.style.display).toBe('none');
  head.dispatchEvent(clickEv());
  expect(full.style.display).toBe('block');
  expect(full.textContent).toBe('我想给mau加一些易用性改动，上工把');
});

test('key 视图——空态出声（不静默留白）', () => {
  window.chatKeyData = { ok: true, count: 0, chars: 0, items: [] };
  window.chatCtxMode = 'key';
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('本次会话暂无关键信息');
});

test('key 视图——错误响应原样出声（宿主 error 文本）', () => {
  window.chatKeyData = { ok: false, error: '关键信息快照失败: boom' };
  window.chatCtxMode = 'key';
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('关键信息快照失败: boom');
});

// ── full 视图（完整前文——送入 LLM 的全量消息落档，剥离修饰后还原） ──
function fakeFullData() {
  return {
    ok: true, count: 3, start: 1, shown: 3, chars: 300, session: 'majordomo',
    path: 'D:/Data/sessions_ctx/majordomo_20261002_120000.txt',
    tokens: 125, ratio: 2.4, estimated: false,
    items: [
      { i: 1, role: 'system', tool: '', chars: 200, time: 0, truncated: false, preview: '注入内容', content: '注入内容全文' },
      { i: 2, role: 'user', tool: '', chars: 60, time: 0, truncated: false, preview: '你好', content: '你好' },
      { i: 3, role: 'tool', tool: 'text-read', chars: 40, time: 0, truncated: false, preview: '结果', content: '结果全文' }
    ]
  };
}

test('full 视图——标题 / meta / 四类角色中文标签', () => {
  window.chatFullData = fakeFullData();
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxTitle').textContent).toBe('完整前文');
  expect(document.getElementById('ctxMeta').textContent).toContain('共 3 条');
  const rows = document.querySelectorAll('#ctxList .ctx-item');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('.ctx-role').textContent).toBe('系统');
  expect(rows[0].querySelector('.ctx-role').className).toContain('ctx-role-system');
  expect(rows[1].querySelector('.ctx-role').textContent).toBe('用户');
  expect(rows[2].querySelector('.ctx-role').textContent).toBe('工具');
  expect(rows[2].querySelector('.ctx-tool').textContent).toBe('text-read');
});

test('full 视图——空态出声（不静默留白）', () => {
  window.chatFullData = { ok: true, count: 0, chars: 0, items: [] };
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('本次会话暂无完整前文');
});

test('full 视图——错误响应原样出声（宿主 error 文本）', () => {
  window.chatFullData = { ok: false, error: '完整前文解析失败: 字段 content 正文越界' };
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxList').textContent).toContain('完整前文解析失败');
});

test('视图切换——已开时切到完整前文读 /api/v1/fullctx', () => {
  let url = '';
  vi.stubGlobal('fetch', (u) => { url = u; return Promise.resolve({ json: () => Promise.resolve(fakeFullData()) }); });
  window.chatInfoSet('s', 3, 1200);
  document.getElementById('chatCtxCount').dispatchEvent(clickEv());
  document.getElementById('ctxModeFull').dispatchEvent(clickEv());
  expect(window.chatCtxMode).toBe('full');
  expect(url).toBe('/api/v1/fullctx');
  expect(document.getElementById('ctxTitle').textContent).toBe('完整前文');
  expect(document.getElementById('ctxModeFull').className).toContain('ctx-mode-on');
  vi.unstubAllGlobals();
});

test('full 视图——token 按宿主折算值直显（实测比值 + 字符数保留可追溯）', () => {
  window.chatFullData = fakeFullData();
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxMeta').textContent).toContain('约 125 tokens（实测比值 2.40 折算）');
  expect(document.getElementById('ctxMeta').textContent).toContain('300 字符');
});

test('full 视图——无实测回落缺省 2.4 并标注（不冒充实测）', () => {
  const d = fakeFullData();
  d.estimated = true;
  window.chatFullData = d;
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxMeta').textContent).toContain('缺省比值 2.4 折算——本会话无实测');
});

test('full 视图——宿主未回折算值即出声（不静默留白）', () => {
  const d = fakeFullData();
  d.tokens = 0;
  window.chatFullData = d;
  window.chatCtxMode = 'full';
  window.chatCtxRender();
  expect(document.getElementById('ctxMeta').textContent).toContain('token 未知');
});

test('弹层刷新按钮——点击拉完整前文（采集就绪）+ 全量重拉会话页面', () => {
  const urls = [];
  vi.stubGlobal('fetch', (u) => {
    urls.push(u);
    if (u.indexOf('/api/v1/history') === 0) {
      return Promise.resolve({ json: () => Promise.resolve({ version: 1, sessionId: 'majordomo', gen: 1, count: 0, start: 0, blocks: [] }) });
    }
    return Promise.resolve({ json: () => Promise.resolve(fakeFullData()) });
  });
  window.chatCtxMode = 'full';
  document.getElementById('ctxRefresh').dispatchEvent(clickEv());
  expect(urls).toContain('/api/v1/fullctx');
  expect(urls).toContain('/api/v1/history?max=2000');
  vi.unstubAllGlobals();
});

test('状态栏第三段——「前文关键信息」渲染 + 点击开层读 /api/v1/keyinfo', () => {
  let url = '';
  vi.stubGlobal('fetch', (u) => { url = u; return Promise.resolve({ json: () => Promise.resolve(fakeKeyData()) }); });
  window.chatInfoSet('s', 3, 1200);
  const k = document.getElementById('chatKeyInfo');
  expect(k.textContent).toBe('前文关键信息');
  k.dispatchEvent(clickEv());
  expect(document.getElementById('ctxPopover').style.display).toBe('block');
  expect(url).toBe('/api/v1/keyinfo');
  expect(document.getElementById('ctxModeKey').className).toContain('ctx-mode-on');
  vi.unstubAllGlobals();
});

test('视图切换——已开时切到关键信息复用缓存，不重复读取', () => {
  let calls = 0;
  vi.stubGlobal('fetch', () => { calls = calls + 1; return Promise.resolve({ json: () => Promise.resolve(fakeData()) }); });
  window.chatInfoSet('s', 3, 1200);
  document.getElementById('chatCtxCount').dispatchEvent(clickEv());
  expect(calls).toBe(1);
  window.chatKeyData = fakeKeyData();
  document.getElementById('chatKeyInfo').dispatchEvent(clickEv());
  expect(window.chatCtxMode).toBe('key');
  expect(document.getElementById('ctxTitle').textContent).toBe('前文关键信息');
  expect(calls).toBe(1);
  vi.unstubAllGlobals();
});
