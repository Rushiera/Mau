// tests/unit/chat-tools.test.mjs —— 工具块渲染（骨架层 + 逐工具填充单例 + 回落）单元测试
// 依据：Project/CH4/design-ch4-frontend-tools.md §九（分派 / 骨架 / 填充 / 超长 / 交互 / 回落）
// 骨架 8 类本期全覆盖：exec / diagnostics / listing / matches / lines / file / json / text
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
test('分派入口——未登记工具落探测骨架（非空；仅 name 非法时返回 null）', () => {
  expect(window.chatToolBody(null)).toBeNull();
  expect(window.chatToolBody({ name: '', arguments: '{}', result: 'x' })).toBeNull();
  // 骨架落位表命中（text-read → file）
  const b = window.chatToolBody({ name: 'text-read', arguments: '{}', result: 'x' });
  expect(b).not.toBeNull();
  expect(b.segs.length).toBe(2);
  // 完全未登记——探测骨架（永不空白）
  const u = window.chatToolBody({ name: '未知工具', arguments: '{}', result: 'x' });
  expect(u).not.toBeNull();
  expect(u.segs.length).toBe(2);
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

test('骨架全覆盖——8 类骨架均产出输入 / 输出两段', () => {
  const cases = [
    { name: 'powershell', arguments: '{"command":"x"}', result: '{"exit":0,"stdout":"a","stderr":""}' },
    { name: 'cs-build', arguments: '{}', result: '{"ok":true,"errors":0,"warnings":0}' },
    { name: 'text-tree', arguments: '{}', result: 'a/\na\\b.txt' },
    { name: 'text-grep', arguments: '{}', result: 'a.cs:1:x' },
    { name: 'text-read_lines', arguments: '{}', result: '1: x' },
    { name: 'text-read', arguments: '{}', result: 'x' },
    { name: 'host-flows', arguments: '{}', result: '{"a":1}' },
    { name: 'text-write', arguments: '{}', result: 'OK' }
  ];
  for (const c of cases) {
    const b = window.chatToolBody(c);
    expect(b).not.toBeNull();
    expect(b.segs.length).toBe(2);
    expect(b.segs[0].classList.contains('seg-in')).toBe(true);
    expect(b.segs[1].classList.contains('seg-out')).toBe(true);
  }
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
  // 失败态走 err 配色（规格 §七）
  expect(out.querySelector('.tr.err')).not.toBeNull();
});

test('exec 骨架——无 command 的工具（host-reload）走通用键值输入段', () => {
  const card = renderTool({
    name: 'host-reload',
    arguments: JSON.stringify({ cat: 'TextCat' }),
    result: 'reload TextCat: #12 → #13',
    summary: '重载 Flow "TextCat"'
  });
  const inSeg = card.querySelector('.seg-in');
  expect(inSeg.querySelectorAll('.seg-kv').length).toBe(1);
  expect(inSeg.querySelector('.seg-kv-k').textContent).toBe('cat: ');
  expect(inSeg.querySelector('.seg-kv-v').textContent).toBe('TextCat');
  expect(card.querySelector('.seg-out .tr').textContent).toContain('#12 → #13');
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
  // 图标不再逐工具区分（2026-09-18）——按骨架取（exec → 💻）；双线差异由版本标签承担
  expect(card5.querySelector('.tn').textContent.startsWith('💻')).toBe(true);
  expect(card7.querySelector('.tn').textContent.startsWith('💻')).toBe(true);
});

// ── diagnostics 骨架（诊断列表）──
test('diagnostics 骨架——JSON 计数徽标（ok 态不标失败）', () => {
  const card = renderTool({
    name: 'cs-build',
    arguments: JSON.stringify({ path: 'X.csproj' }),
    result: JSON.stringify({ ok: true, errors: 0, warnings: 2 }),
    summary: 'C#构建 "X.csproj" → OK（共0错 2警）'
  });
  const cap = card.querySelector('.seg-out .seg-cap').textContent;
  expect(cap).toContain('0 错 2 警');
  expect(cap).not.toContain('失败');
});

test('diagnostics 骨架——失败态徽标 + 诊断逐行可见', () => {
  const card = renderTool({
    name: 'cs-check',
    arguments: '{}',
    result: '{"ok":false,"tool":"cs-check","errors":3,"warnings":1}\nA.cs:1:1: CS1002: 需要 ;'
  });
  const cap = card.querySelector('.seg-out .seg-cap').textContent;
  expect(cap).toContain('3 错 1 警');
  expect(cap).toContain('失败');
});

test('diagnostics 骨架——纯文本清单退化行数徽标（解析不出计数就不标）', () => {
  const card = renderTool({
    name: 'cs-comment_check',
    arguments: JSON.stringify({ path: 'X.csproj' }),
    result: 'A.cs:12: 缺少 summary\nB.cs:34: 缺少 summary',
    summary: 'C#注释检查 → "共2处缺注释"'
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('2 行');
  expect(card.querySelectorAll('.seg-out .tr').length).toBe(2);
});

// ── listing 骨架（列举）──
test('listing 骨架——条目计数排除提示行 + 逐行等宽可见', () => {
  const card = renderTool({
    name: 'text-tree',
    arguments: JSON.stringify({ path: 'CCBP' }),
    result: 'L1/\nL1\\Tree.md\n[git] 存在 .git',
    summary: '展开目录 "CCBP" → "1文件 1目录"'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('2 条目');
  expect(out.querySelectorAll('.seg-line').length).toBe(2);
});

test('listing 骨架——空结果显式占位', () => {
  const card = renderTool({
    name: 'text-find',
    arguments: JSON.stringify({ dir: 'X', pattern: '*.zzz' }),
    result: '',
    summary: '搜索文件 "X" → 无结果'
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('无输出');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('（无输出）');
});

// ── matches 骨架（检索命中）──
test('matches 骨架——text-grep 路径 / 行号 / 上下文 三列', () => {
  const card = renderTool({
    name: 'text-grep',
    arguments: JSON.stringify({ dir: 'src', keyword: 'foo' }),
    result: 'src/A.cs:12:foo bar\nsrc/B.cs:7:baz foo',
    summary: '检索内容 "src" 含 "foo" → 2文件·2命中'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('2 命中');
  const hits = out.querySelectorAll('.seg-hit');
  expect(hits.length).toBe(2);
  expect(hits[0].querySelector('.seg-hit-path').textContent).toBe('src/A.cs');
  expect(hits[0].querySelector('.seg-hit-line').textContent).toBe(':12');
  expect(hits[0].querySelector('.seg-hit-ctx').textContent).toBe(':foo bar');
});

test('matches 骨架——无行号前缀的行整行渲染（不丢行）', () => {
  const card = renderTool({
    name: 'cs-find_ref',
    arguments: '{}',
    result: 'Program.cs\n[跨程序集] A.cs:9:Use()'
  });
  const hits = card.querySelectorAll('.seg-out .seg-hit');
  expect(hits.length).toBe(2);
  expect(hits[0].querySelector('.seg-hit-path').textContent).toBe('Program.cs');
  expect(hits[0].querySelector('.seg-hit-line')).toBeNull();
  expect(hits[1].querySelector('.seg-hit-line').textContent).toBe(':9');
});

// ── lines 骨架（带行号文本）──
test('lines 骨架——行号列与内容分列', () => {
  const card = renderTool({
    name: 'text-read_lines',
    arguments: JSON.stringify({ path: 'a.md', start: 1, end: 2 }),
    result: '1: # 标题\n2: 正文',
    summary: '按行读取 "a.md" "第 1 行到第 2 行" → …'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('2 行');
  const rows = out.querySelectorAll('.seg-line');
  expect(rows.length).toBe(2);
  expect(rows[0].querySelector('.seg-line-no').textContent).toBe('1');
  expect(rows[0].querySelector('.seg-line-tx').textContent).toBe('# 标题');
});

// ── file 骨架（文件内容）──
test('file 骨架——正文 + 规模标注（字符 / 行）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: 'line1\nline2',
    summary: '读取文件 "a.txt" → "line1（2行 · 11 B）"'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('11 字符 / 2 行');
  expect(out.querySelector('.tr').textContent).toBe('line1\nline2');
});

// ── json 骨架（通用结构）──
test('json 骨架——JSON 结果拆键值表（键 / 值分色）', () => {
  const card = renderTool({
    name: 'host-flows',
    arguments: '{}',
    result: JSON.stringify({ id: 'TextCat', kind: 'Flow', alive: true }),
    summary: 'Flow 清单 → 3 项'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('3 键');
  const kvs = out.querySelectorAll('.seg-kv');
  expect(kvs.length).toBe(3);
  expect(kvs[0].querySelector('.seg-kv-k').textContent).toBe('id: ');
  expect(kvs[0].querySelector('.seg-kv-v').textContent).toBe('TextCat');
});

// ── text 骨架（纯文本兜底）──
test('text 骨架——原文 + 行数徽标', () => {
  const card = renderTool({
    name: 'text-write',
    arguments: JSON.stringify({ path: 'a.txt', content: 'x' }),
    result: 'OK',
    summary: '写入文件 "a.txt" → OK'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('1 行');
  expect(out.querySelector('.tr').textContent).toBe('OK');
});

// ── 形态探测回落（完全未登记工具）──
test('探测回落——JSON 结果走 json 骨架；纯文本走 text 骨架', () => {
  const c1 = renderTool({ name: 'weird-tool', arguments: '{"x":"y"}', result: '{"a":1}' }, 70);
  expect(c1.querySelector('.seg-out .seg-cap').textContent).toContain('1 键');
  renderTool({ name: 'weird-tool-2', arguments: '{}', result: '随便一段文本' }, 71);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.seg-out .tr').textContent).toBe('随便一段文本');
});

// ── 回落三态（空 / 失败 / 非法 JSON——规格 §七）──
test('回落三态——空结果显式占位 / ERR 失败配色 / 非法 JSON 原样可见', () => {
  const c1 = renderTool({ name: 'text-read', arguments: '{}', result: '' }, 72);
  expect(c1.querySelector('.seg-out .seg-cap').textContent).toContain('无输出');
  expect(c1.querySelector('.seg-out .tr').textContent).toBe('（无输出）');

  renderTool({ name: 'text-delete', arguments: '{}', result: 'ERR|NO_SUCH_FILE' }, 73);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.seg-out .seg-cap').textContent).toContain('失败');
  expect(c2.querySelector('.seg-out .tr.err')).not.toBeNull();

  renderTool({ name: 'config-get', arguments: '{}', result: '{坏 JSON' }, 74);
  const c3 = chatMsgs.querySelectorAll('.chat-tool')[2];
  expect(c3.querySelector('.seg-out .seg-cap').textContent).toContain('原始输出');
  expect(c3.querySelector('.seg-out .tr').textContent).toBe('{坏 JSON');
});

test('回落——处理中（无 result）显式占位，不产生空段', () => {
  const card = renderTool({ name: 'text-read', arguments: JSON.stringify({ path: 'a.txt' }) }, 75);
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('处理中');
  expect(card.querySelector('.seg-out .ta').textContent).toContain('⏳ 处理中…');
  expect(card.querySelector('.seg-out .tr')).toBeNull();
});

// ── 超长与二级展开（规格 §5.2 / §5.3）──
test('段折叠——行数 ≤5 默认展开（短内容不折叠）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: 'l1\nl2\nl3\nl4\nl5',
    summary: '读取文件 "a.txt" → …'
  });
  const out = card.querySelector('.seg-out');
  expect(out.open).toBe(true);
  expect(out.querySelector('.seg-peek')).toBeNull();
});

test('段折叠——行数 >5 默认折叠并显示摘要（前 2 + 折叠提示 + 后 2）', () => {
  const lines = [];
  for (let i = 1; i <= 10; i = i + 1) { lines.push('l' + i); }
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: lines.join('\n'),
    summary: '读取文件 "a.txt" → …'
  });
  const out = card.querySelector('.seg-out');
  expect(out.open).toBe(false);
  const peek = out.querySelector('.seg-peek');
  expect(peek).not.toBeNull();
  // 保留首 2 行与末 2 行
  expect(peek.textContent).toContain('l1\nl2');
  expect(peek.textContent).toContain('l9\nl10');
  // 中间 6 行被折叠（10 - 2 - 2）；字符数按中间段实算（含换行）
  expect(peek.textContent).toContain('折叠了 6 行 17 字符');
  // 展开态全量仍在段内（折叠不截断载荷）
  expect(out.querySelector('.tr').textContent.split('\n').length).toBe(10);
});

test('工具图标——三级回落（工具专属 → 骨架 → ❓）', () => {
  // ② 骨架级——8 类各一
  expect(window.chatToolIcon('text-read')).toBe('📖');
  expect(window.chatToolIcon('cs-build')).toBe('🩺');
  expect(window.chatToolIcon('text-tree')).toBe('📂');
  expect(window.chatToolIcon('text-grep')).toBe('🔍');
  expect(window.chatToolIcon('text-read_lines')).toBe('🔢');
  expect(window.chatToolIcon('host-flows')).toBe('🧩');
  expect(window.chatToolIcon('text-write')).toBe('📝');
  expect(window.chatToolIcon('powershell')).toBe('💻');
  // ① 工具专属覆盖——web-search 归 text 骨架，但有专属互联网图标
  expect(window.chatToolIcon('web-search')).toBe('🌐');
  // ③ 未登记工具 → 问号（本不该出现——显式暴露，不给通用图标掩盖）
  expect(window.chatToolIconBySkeleton('未知工具')).toBe('');
  expect(window.chatToolIcon('未知工具')).toBe('❓');
});

// ── 交互——内层段让位（段折叠头不收整块；段内容仍收）──
test('内层段让位——段折叠头点击不收整块；段内容点击仍收起整块', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a'),
    summary: '执行命令 "Get-Date"'
  });
  card.open = true;
  // 段折叠头——只切该段，不收整块
  card.querySelector('.seg > summary').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(true);
  // 段内容——收起整块（死区判据保护拖选 / 长按）
  card.querySelector('.seg-out .seg-body .tr').dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});

test('内层段让位——外层内容区（非段内）点击收起整块', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a'),
    summary: '执行命令 "Get-Date"'
  });
  card.open = true;
  card.dispatchEvent(new Event('click', { bubbles: true }));
  expect(card.open).toBe(false);
});

// ── 填充覆盖表（TextCat 批——输入意图行自然语言化）──
test('填充覆盖——text-read 输入段为自然语言意图行（非键值表）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a/b.md' }),
    result: 'x',
    summary: '读取文件 "a/b.md"'
  }, 80);
  const inSeg = card.querySelector('.seg-in');
  expect(inSeg.querySelector('.seg-kv')).toBeNull();
  expect(inSeg.querySelector('.seg-line').textContent).toBe('读取 a/b.md');
  expect(inSeg.querySelector('.seg-cap').textContent).toContain('输入 · 1 行');
});

test('填充覆盖——text-replace 输入段三行（标题 / 旧 / 新）', () => {
  const card = renderTool({
    name: 'text-replace',
    arguments: JSON.stringify({ path: 'a.md', old: '旧文本', new: '新文本', mode: 'exact' }),
    result: 'OK 替换完成: 1 处',
    summary: '替换文本 "a.md" → …'
  }, 81);
  const lines = card.querySelectorAll('.seg-in .seg-line');
  expect(lines.length).toBe(3);
  expect(lines[0].textContent).toBe('替换 a.md · 模式 exact');
  expect(lines[1].textContent).toBe('旧：旧文本');
  expect(lines[2].textContent).toBe('新：新文本');
});

test('填充覆盖——text-tree 意图行含 depth/limit；未声明工具仍走键值表', () => {
  const card = renderTool({
    name: 'text-tree',
    arguments: JSON.stringify({ path: 'CCBP', depth: 2, limit: 50 }),
    result: 'a/',
    summary: '展开目录 "CCBP"'
  }, 82);
  expect(card.querySelector('.seg-in .seg-line').textContent).toBe('展开 CCBP · depth 2 · limit 50');
  renderTool({ name: 'mau-verify', arguments: JSON.stringify({ file: 'a.mau' }), result: 'MAU_VERIFY_OK' }, 83);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.seg-in .seg-kv')).not.toBeNull();
});

test('填充覆盖——text-read_between 锚点缺省显式化（(文件头) / (文件尾)）', () => {
  const card = renderTool({
    name: 'text-read_between',
    arguments: JSON.stringify({ path: 'a.md' }),
    result: 'x',
    summary: '区间读取 "a.md"'
  }, 84);
  const lines = card.querySelectorAll('.seg-in .seg-line');
  expect(lines.length).toBe(2);
  expect(lines[1].textContent).toBe('锚点 （文件头） ~ （文件尾）');
});

// ── 折叠行文案（headline——前端自然语言，优先于宿主 summary）──
test('headline——text-read 折叠行为自然语言（无「→」箭头，规模自然拼接）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.md' }),
    result: 'l1\nl2\nl3',
    summary: '读取文件 "a.md" → "l1（3行 · 11 B）"'
  }, 85);
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 a.md · 3 行 8 字符');
});

test('headline——text-grep 命中数进折叠行；未完成（无 result）不标规模', () => {
  const card = renderTool({
    name: 'text-grep',
    arguments: JSON.stringify({ dir: 'src', keyword: 'foo' }),
    result: 'a.cs:1:x\na.cs:2:y',
    summary: '检索内容 "src" 含 "foo" → 1文件·2命中'
  }, 86);
  expect(card.querySelector('.tn').textContent).toBe('🔍 检索 src · 含 foo · 2 命中');
  renderTool({ name: 'text-read_lines', arguments: JSON.stringify({ path: 'a.md', start: 3 }), toolIndex: 1, toolTotal: 1 }, 87);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.tn').textContent).toBe('🔢 按行读取 a.md · L3 起');
});

test('headline——text-replace 处数从结果提取；text-tree 条目数排除提示行', () => {
  const card = renderTool({
    name: 'text-replace',
    arguments: JSON.stringify({ path: 'a.md', old: 'x', new: 'y' }),
    result: 'OK 替换完成: 1 处（a.md）',
    summary: '替换文本 "a.md" → …'
  }, 88);
  expect(card.querySelector('.tn').textContent).toBe('🔄 替换 a.md · 1 处');
  renderTool({ name: 'text-tree', arguments: JSON.stringify({ path: 'CCBP', depth: 2 }), result: 'a/\na\\b.txt\n[git] 存在 .git' }, 89);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.tn').textContent).toBe('📂 展开 CCBP · depth 2 · 2 条目');
});

test('工具专属图标——find 🔍 / move 📦 / delete 🗑️ 覆盖骨架图标', () => {
  expect(window.chatToolIcon('text-find')).toBe('🔍');
  expect(window.chatToolIcon('text-move')).toBe('📦');
  expect(window.chatToolIcon('text-delete')).toBe('🗑️');
  // 未配专属图标的 text-* 仍吃骨架图标
  expect(window.chatToolIcon('text-read')).toBe('📖');
  expect(window.chatToolIcon('text-write')).toBe('📝');
});

test('段折叠摘要——摘要挂 summary 内（details 折叠时可见）', () => {
  const lines = [];
  for (let i = 1; i <= 8; i = i + 1) { lines.push('l' + i); }
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: lines.join('\n'),
    summary: '读取文件 "a.txt"'
  }, 90);
  const out = card.querySelector('.seg-out');
  expect(out.open).toBe(false);
  expect(out.querySelector('summary .seg-peek')).not.toBeNull();
});

// ── 结构化返回头（CsCat 样板三件——首行 JSON 元数据 + 正文定界行）──
test('结构化头——chatMetaHead 拆分首行 JSON 与正文；非结构化结果返回 null', () => {
  const h = window.chatMetaHead('{"ok":true,"tool":"cs-check","errors":0,"warnings":2}\nA.cs:3:1: CS1026: 缺少 )');
  expect(h.meta.tool).toBe('cs-check');
  expect(h.body).toBe('A.cs:3:1: CS1026: 缺少 )');
  expect(window.chatMetaHead('OK 项目 X 语法 0 错误')).toBeNull();
  expect(window.chatMetaHead('{"a":1}')).toBeNull();
});

test('diagnostics + 结构化头——计数徽标由 meta 驱动（正文只承载诊断行）', () => {
  const card = renderTool({
    name: 'cs-check',
    arguments: JSON.stringify({ path: 'X.csproj' }),
    result: '{"ok":false,"tool":"cs-check","project":"X","files":3,"errors":2,"warnings":1}\nA.cs:3:1: CS1026: 缺少 )\nB.cs:9:5: CS1002: 需要 ;',
    summary: 'C#检查 "X.csproj" → …'
  }, 91);
  const cap = card.querySelector('.seg-out .seg-cap').textContent;
  expect(cap).toContain('2 错 1 警');
  expect(cap).toContain('失败');
  expect(card.querySelectorAll('.seg-out .tr').length).toBe(2);
});

test('lines + 结构化头——行号列按 meta.start 计算，行尾标注剥离显示', () => {
  const card = renderTool({
    name: 'cs-read',
    arguments: JSON.stringify({ path: 'X.csproj', class: 'A', member: 'M' }),
    result: '{"ok":true,"tool":"cs-read","file":"C:\\\\x\\\\A.cs","class":"A","member":"M","start":612,"end":614}\n        private void M() // L612\n        { // L613\n        } // L614',
    summary: 'C#读取 "A"."M"'
  }, 92);
  const rows = card.querySelectorAll('.seg-out .seg-line');
  expect(rows.length).toBe(3);
  expect(rows[0].querySelector('.seg-line-no').textContent).toBe('612');
  expect(rows[0].querySelector('.seg-line-tx').textContent).toBe('        private void M()');
  expect(rows[2].querySelector('.seg-line-no').textContent).toBe('614');
});

test('headline——cs-check / cs-build / cs-read 全由结构化字段拼接', () => {
  const c1 = renderTool({ name: 'cs-check', arguments: JSON.stringify({ path: 'Git:mau/CatHome4/X.csproj' }), result: '{"ok":true,"tool":"cs-check","errors":0,"warnings":0}' }, 93);
  expect(c1.querySelector('.tn').textContent).toBe('🩺 语法检查 X.csproj · 0 错 0 警');
  renderTool({ name: 'cs-build', arguments: JSON.stringify({ path: 'C:/a/B.csproj' }), result: '{"ok":true,"tool":"cs-build","project":"B","exit":0,"errors":0,"warnings":0,"ms":4600}', summary: 'C#构建' }, 94);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('🩺 编译 B.csproj · 成功 0 错 0 警 · 4.6s');
  renderTool({ name: 'cs-read', arguments: JSON.stringify({ path: 'X.csproj', class: 'A', member: 'M' }), result: '{"ok":true,"tool":"cs-read","file":"x","class":"A","member":"M","start":26,"end":37}' }, 95);
  const c3 = chatMsgs.querySelectorAll('.chat-tool')[2];
  expect(c3.querySelector('.tn').textContent).toBe('🔢 读取 A.M · L26-37');
});

test('结构化失败——ok:false 触发红色标记（卡片 err 类 + 折叠头「· 失败」单次）', () => {
  const card = renderTool({
    name: 'cs-check',
    arguments: JSON.stringify({ path: 'Bad.csproj' }),
    result: '{"ok":false,"tool":"cs-check","project":"Bad","files":1,"errors":1,"warnings":0}\nBad.cs:3:19: CS1026: 应输入 )',
    summary: 'C#检查'
  }, 96);
  expect(card.classList.contains('err')).toBe(true);
  const cap = card.querySelector('.seg-out .seg-cap').textContent;
  expect(cap).toContain('1 错 0 警');
  expect(cap.split('失败').length - 1).toBe(1);
  expect(card.querySelector('.seg-out .tr.err')).not.toBeNull();
});

// ── CsCat 其余 8 件（结构化头驱动）──
test('cs-list / cs-dead / cs-comment_check / cs-format——headline 由结构化字段拼', () => {
  const c1 = renderTool({ name: 'cs-list', arguments: JSON.stringify({ path: 'Git:mau/X.csproj' }), result: '{"ok":true,"tool":"cs-list","project":"CatHome4.Core","classes":19}' }, 100);
  expect(c1.querySelector('.tn').textContent).toBe('📂 列出 CatHome4.Core · 19 类');
  renderTool({ name: 'cs-dead', arguments: JSON.stringify({ path: 'Git:mau/X.csproj' }), result: '{"ok":true,"tool":"cs-dead","dead":1,"scanned":37}' }, 101);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('🩺 零引用扫描 X.csproj · 1 处待清');
  renderTool({ name: 'cs-comment_check', arguments: JSON.stringify({ path: 'Git:mau/X.csproj' }), result: '{"ok":true,"tool":"cs-comment_check","checked":348,"missing":0}' }, 102);
  const c3 = chatMsgs.querySelectorAll('.chat-tool')[2];
  expect(c3.querySelector('.tn').textContent).toBe('🩺 注释检查 X.csproj · 齐全 348 项');
  renderTool({ name: 'cs-format', arguments: JSON.stringify({ path: 'Git:mau/A.cs', mode: 'check' }), result: '{"ok":true,"tool":"cs-format","mode":"check","files":1,"changedFiles":0,"changedLines":0,"failedFiles":0}' }, 103);
  const c4 = chatMsgs.querySelectorAll('.chat-tool')[3];
  expect(c4.querySelector('.tn').textContent).toBe('🩺 格式哨兵 A.cs · 需规整 0 文件 / 0 行');
});

test('cs-find_ref——三列含列号 + [跨程序集] 徽标；headline 命中数', () => {
  const card = renderTool({
    name: 'cs-find_ref',
    arguments: JSON.stringify({ path: 'Git:mau/CatHome4.sln', class: 'X', member: 'Y' }),
    result: '{"ok":true,"tool":"cs-find_ref","class":"X","member":"Y","hits":2,"projects":9}\nA.cs:228:55: payload["summary"] = Build(...)\n[跨程序集] B.cs:20:45: string s = Build("text-read", ...)',
    summary: 'C#查找引用'
  }, 104);
  expect(card.querySelector('.tn').textContent).toBe('🔍 引用 X.Y · 2 处 · 9 项目');
  const hits = card.querySelectorAll('.seg-out .seg-hit');
  expect(hits.length).toBe(2);
  expect(hits[0].querySelector('.seg-hit-path').textContent).toBe('A.cs');
  expect(hits[0].querySelector('.seg-hit-line').textContent).toBe(':228:55');
  expect(hits[1].querySelector('.seg-hit-cross')).not.toBeNull();
  expect(hits[1].querySelector('.seg-hit-line').textContent).toBe(':20:45');
});

test('写类三件——outputLines 由 meta 生成自然语言；无正文不留白', () => {
  const card = renderTool({
    name: 'cs-member',
    arguments: JSON.stringify({ path: 'X.csproj', class: 'A', op: 'insert' }),
    result: '{"ok":true,"tool":"cs-member","op":"insert","class":"A","file":"A.cs","start":123,"end":140,"kind":"MethodDeclarationSyntax"}',
    summary: 'C#成员操作'
  }, 105);
  expect(card.querySelector('.tn').textContent).toBe('📝 插入 A · 落盘 L123-140');
  expect(card.querySelector('.seg-out .seg-line').textContent).toContain('已落盘 A.cs · L123-140');
  renderTool({ name: 'cs-comment', arguments: JSON.stringify({ path: 'X.csproj', class: 'A', member: 'M', type: 'summary' }), result: '{"ok":true,"tool":"cs-comment","class":"A","member":"M","type":"summary"}' }, 106);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('📝 注释 A.M · summary');
  expect(c2.querySelector('.seg-out .seg-line').textContent).toContain('已写入 A.M 的 summary 注释');
});
