// tests/unit/chat-tools.test.mjs —— 工具块渲染（骨架层 + 逐工具填充单例 + 回落）单元测试
// 依据：Project/CH4/design-ch4-frontend-tools.md §九（分派 / 骨架 / 填充 / 超长 / 交互 / 回落）
// 加载方式：node 环境 + 手动 JSDOM（与 chat-view.test.mjs 同构；模块清单须与 chat.html 引导层一致）
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

let chatMsgs;

beforeAll(async () => {
  const html = await readFile(new URL('../../chat.html', import.meta.url), 'utf-8');
  const dom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  globalThis.window = globalThis;
  globalThis.document = dom.window.document;
  globalThis.Node = dom.window.Node;
  globalThis.Event = dom.window.Event;
  globalThis.HTMLElement = dom.window.HTMLElement;
  const mods = ['ui-common.js', 'chat-md.js', 'chat-cmd.js', 'chat-tools.js', 'chat-view.js', 'chat-core.js', 'chat-note.js'];
  for (const m of mods) {
    const code = await readFile(new URL('../../js/' + m, import.meta.url), 'utf-8');
    vm.runInThisContext(code, { filename: m });
  }
  chatMsgs = document.getElementById('chatMsgs');
});

beforeEach(() => {
  window.chatState = 'sending';
  window.viewContainers = {};
  window.chatPending = [];
  if (window.chatTimer) { clearTimeout(window.chatTimer); window.chatTimer = null; }
  chatMsgs.textContent = '';
  window.chatPhaseReset();
});

// 工具卡渲染快捷入口——返回 details.chat-tool
function renderTool(payload, seq) {
  window.chatOnView({ seq: seq || 60, renderType: 'toolcard', payload: payload, replaceSeq: -1 });
  return chatMsgs.querySelector('.chat-tool');
}

function psResult(exit, stdout, stderr, truncated, timeout) {
  return JSON.stringify({ exit: exit, stdout: stdout || '', stderr: stderr || '', truncated: truncated === true, timeout: timeout === true });
}

// ── 分派面（chatToolBody 直调）──
test('分派入口——未登记工具返回 null（调用方走回落）', () => {
  expect(window.chatToolBody(null)).toBeNull();
  expect(window.chatToolBody({ name: 'text-read', arguments: '{}', result: 'x' })).toBeNull();
  expect(window.chatToolBody({ name: '未知工具', arguments: '{}', result: 'x' })).toBeNull();
});

test('分派入口——powershell / powershell7 命中 exec 填充（两段 + 版本标签）', () => {
  const b5 = window.chatToolBody({ name: 'powershell', arguments: '{}', result: '{"exit":0,"stdout":"","stderr":""}' });
  const b7 = window.chatToolBody({ name: 'powershell7', arguments: '{}', result: '{"exit":0,"stdout":"","stderr":""}' });
  expect(b5.segs.length).toBe(2);
  expect(b5.tag).toBe('PS 5.1');
  expect(b5.tagCls).toBe('ps5');
  expect(b7.tag).toBe('PS 7');
  expect(b7.tagCls).toBe('ps7');
});

// ── exec 骨架形态 ──
test('exec 骨架——输入段（命令原文）+ 输出段（exit 徽标 + stdout 分块）', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, '2026-09-18 14:00:00'),
    summary: '执行命令 "Get-Date"'
  });
  const segs = card.querySelectorAll('.seg');
  expect(segs.length).toBe(2);
  expect(segs[0].classList.contains('seg-in')).toBe(true);
  expect(segs[1].classList.contains('seg-out')).toBe(true);
  // 输入段——折叠头给规模；段内保留 .ta 原文锚点
  expect(segs[0].querySelector('.seg-cap').textContent).toContain('输入 · 命令 8 字符');
  expect(segs[0].querySelector('.ta').textContent).toBe('Get-Date');
  // 输出段——exit 徽标 + stdout 分块（含规模标注）
  expect(segs[1].querySelector('.seg-cap').textContent).toContain('exit 0');
  expect(segs[1].querySelector('.seg-sec-cap').textContent).toContain('stdout');
  expect(segs[1].querySelector('.tr').textContent).toBe('2026-09-18 14:00:00');
});

test('exec 骨架——stderr 独立分块（err 类）且折叠头标注非零退出码', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'bad-cmd' }),
    result: psResult(1, '', 'command not found')
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('exit 1');
  const errBlock = out.querySelector('.tr.err');
  expect(errBlock).not.toBeNull();
  expect(errBlock.textContent).toBe('command not found');
});

test('exec 骨架——cwd 前置一行（指定 cwd 时标注）', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'git status', cwd: 'C:\\repo' }),
    result: psResult(0, 'ok')
  });
  const inSeg = card.querySelector('.seg-in');
  expect(inSeg.querySelector('.seg-cap').textContent).toContain('指定 cwd');
  expect(inSeg.querySelector('.ta').textContent).toContain('cwd: C:\\repo');
  expect(inSeg.querySelector('.ta').textContent).toContain('git status');
});

test('exec 骨架——截断 + 超时显式（折叠头徽标 + 段内警示）', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-ChildItem -Recurse' }),
    result: psResult(0, 'y'.repeat(16384), '', true, true)
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('⚠️ 已截断');
  const warn = card.querySelector('.seg-out .ta.warn');
  expect(warn).not.toBeNull();
  expect(warn.textContent).toContain('被截断');
  expect(warn.textContent).toContain('超时');
});

test('exec 骨架——空输出显式标注（不留白）', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, '', '')
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('无输出');
});

test('exec 骨架——非 JSON 结果原文可见（不静默丢弃）', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'x' }),
    result: 'ERR|PS_LAUNCH|Win32Exception|boom'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('原始输出');
  expect(out.querySelector('.tr').textContent).toContain('boom');
});

// ── PowerShell 双线显式区分 ──
test('双线区分——powershell 与 powershell7 的折叠行标签 / 类 / 图标各不相同', () => {
  const card5 = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a'),
    summary: '执行命令 "Get-Date"'
  }, 61);
  expect(card5.querySelector('.tn').textContent).toContain('PS 5.1');
  expect(card5.querySelector('.ps-tag').classList.contains('ps5')).toBe(true);

  renderTool({
    name: 'powershell7',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a'),
    summary: '执行命令 "Get-Date"'
  }, 62);
  const card7 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card7.querySelector('.tn').textContent).toContain('PS 7');
  expect(card7.querySelector('.ps-tag').classList.contains('ps7')).toBe(true);
  // 图标区分（💻 5.1 / 💠 7）——emoji 为代理对，按 startsWith 判（charAt(0) 只取半个码元）
  expect(card5.querySelector('.tn').textContent.startsWith('💻')).toBe(true);
  expect(card7.querySelector('.tn').textContent.startsWith('💠')).toBe(true);
});

// ── 回落路径（未登记工具保持现行渲染）──
test('回落——未登记工具不产生段结构（.ta / .tr 直挂卡内）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: '文件内容',
    summary: '读取文件 a.txt'
  });
  expect(card.querySelectorAll('.seg').length).toBe(0);
  expect(card.querySelector('.ta').textContent).toContain('a.txt');
  expect(card.querySelector('.tr').textContent).toBe('文件内容');
  expect(card.querySelector('.tn').textContent).toBe('📖 读取文件 a.txt');
});

// ── 交互——内层段让位（段内点击不收整块）──
test('内层段让位——段内点击不收整块；外层内容区点击仍收起', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a'),
    summary: '执行命令 "Get-Date"'
  });
  card.open = true;
  card.querySelector('.seg').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(true);
  card.querySelector('.seg-out .seg-body').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(true);
  // 外层（卡自身，非段内 / 非折叠头 / 非交互元素）→ 收起
  card.dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});
