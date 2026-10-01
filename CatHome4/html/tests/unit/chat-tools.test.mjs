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
    { name: 'file-tree', arguments: '{}', result: 'a/\na\\b.txt' },
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
    result: psResult(0, '2026-09-18 14:00:00')
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

test('exec 骨架——无 command 的工具（powershell 仅 cwd）走通用键值输入段', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ cwd: 'C:\\work' }),
    result: '{"exit":0,"stdout":"ok","stderr":"","truncated":false,"timeout":false}'
  });
  const inSeg = card.querySelector('.seg-in');
  expect(inSeg.querySelectorAll('.seg-kv').length).toBe(1);
  expect(inSeg.querySelector('.seg-kv-k').textContent).toBe('cwd: ');
  expect(inSeg.querySelector('.seg-kv-v').textContent).toBe('C:\\work');
  expect(card.querySelector('.seg-out .tr').textContent).toContain('ok');
});

// ── PowerShell 双线显式区分 ──
test('双线区分——powershell 与 powershell7 的折叠行标签 / 类 / 图标各不相同', () => {
  const card5 = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a')
  }, 61);
  expect(card5.querySelector('.tn').textContent).toContain('PS 5.1');
  expect(card5.querySelector('.ps-tag').classList.contains('ps5')).toBe(true);

  renderTool({
    name: 'powershell7',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: psResult(0, 'a')
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
    result: JSON.stringify({ ok: true, errors: 0, warnings: 2 })
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
    result: 'A.cs:12: 缺少 summary\nB.cs:34: 缺少 summary'
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('2 行');
  expect(card.querySelectorAll('.seg-out .tr').length).toBe(2);
});

// ── listing 骨架（列举）──
test('listing 骨架——条目计数排除提示行 + 逐行等宽可见', () => {
  const card = renderTool({
    name: 'file-tree',
    arguments: JSON.stringify({ path: 'CCBP' }),
    result: 'L1/\nL1\\Tree.md\n[git] 存在 .git'
  });
  const out = card.querySelector('.seg-out');
  expect(out.querySelector('.seg-cap').textContent).toContain('2 条目');
  expect(out.querySelectorAll('.seg-line').length).toBe(2);
});

test('listing 骨架——空结果显式占位', () => {
  const card = renderTool({
    name: 'file-find',
    arguments: JSON.stringify({ dir: 'X', pattern: '*.zzz' }),
    result: ''
  });
  expect(card.querySelector('.seg-out .seg-cap').textContent).toContain('无输出');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('（无输出）');
});

// ── matches 骨架（检索命中）──
test('matches 骨架——text-grep 路径 / 行号 / 上下文 三列', () => {
  const card = renderTool({
    name: 'text-grep',
    arguments: JSON.stringify({ dir: 'src', keyword: 'foo' }),
    result: 'src/A.cs:12:foo bar\nsrc/B.cs:7:baz foo'
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
    result: '1: # 标题\n2: 正文'
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
    result: 'line1\nline2'
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
    result: JSON.stringify({ id: 'TextCat', kind: 'Flow', alive: true })
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
    result: 'OK'
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

  renderTool({ name: 'file-delete', arguments: '{}', result: 'ERR|NO_SUCH_FILE' }, 73);
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
    result: 'l1\nl2\nl3\nl4\nl5'
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
    result: lines.join('\n')
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
  expect(window.chatToolIcon('file-tree')).toBe('📂');
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
    result: psResult(0, 'a')
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
    result: psResult(0, 'a')
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
    result: 'x'
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
    result: 'OK 替换完成: 1 处'
  }, 81);
  const lines = card.querySelectorAll('.seg-in .seg-line');
  expect(lines.length).toBe(3);
  expect(lines[0].textContent).toBe('替换 a.md · 模式 exact');
  expect(lines[1].textContent).toBe('旧：旧文本');
  expect(lines[2].textContent).toBe('新：新文本');
});

test('填充覆盖——file-tree 意图行含 depth/limit；未声明工具仍走键值表', () => {
  const card = renderTool({
    name: 'file-tree',
    arguments: JSON.stringify({ path: 'CCBP', depth: 2, limit: 50 }),
    result: 'a/'
  }, 82);
  expect(card.querySelector('.seg-in .seg-line').textContent).toBe('展开 CCBP · depth 2 · limit 50');
  renderTool({ name: 'powershell', arguments: JSON.stringify({ cwd: 'C:\\work' }), result: '{"exit":0,"stdout":"","stderr":"","truncated":false,"timeout":false}' }, 83);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.seg-in .seg-kv')).not.toBeNull();
});

test('填充覆盖——text-read_between 锚点缺省显式化（(文件头) / (文件尾)）', () => {
  const card = renderTool({
    name: 'text-read_between',
    arguments: JSON.stringify({ path: 'a.md' }),
    result: 'x'
  }, 84);
  const lines = card.querySelectorAll('.seg-in .seg-line');
  expect(lines.length).toBe(2);
  expect(lines[1].textContent).toBe('锚点 （文件头） ~ （文件尾）');
});

// ── 折叠行文案（headline——前端自然语言，折叠行唯一产出）──
test('headline——text-read 折叠行为自然语言（无「→」箭头，规模自然拼接）', () => {
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.md' }),
    result: 'l1\nl2\nl3'
  }, 85);
  expect(card.querySelector('.tn').textContent).toBe('📖 读取 a.md · 3 行 8 字符');
});

test('headline——text-grep 命中数进折叠行；未完成（无 result）不标规模', () => {
  const card = renderTool({
    name: 'text-grep',
    arguments: JSON.stringify({ dir: 'src', keyword: 'foo' }),
    result: 'a.cs:1:x\na.cs:2:y'
  }, 86);
  expect(card.querySelector('.tn').textContent).toBe('🔍 检索 src · 含 foo · 2 命中');
  renderTool({ name: 'text-read_lines', arguments: JSON.stringify({ path: 'a.md', start: 3 }), toolIndex: 1, toolTotal: 1 }, 87);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.tn').textContent).toBe('🔢 按行读取 a.md · L3 起');
});

test('headline——text-replace 处数从结果提取；file-tree 条目数排除提示行', () => {
  const card = renderTool({
    name: 'text-replace',
    arguments: JSON.stringify({ path: 'a.md', old: 'x', new: 'y' }),
    result: 'OK 替换完成: 1 处（a.md）'
  }, 88);
  expect(card.querySelector('.tn').textContent).toBe('🔄 替换 a.md · 1 处');
  renderTool({ name: 'file-tree', arguments: JSON.stringify({ path: 'CCBP', depth: 2 }), result: 'a/\na\\b.txt\n[git] 存在 .git\n[skip] 3 个忽略目录（.git/bin/obj/node_modules 等）未扫描——条目在其内已跳过\n[截断] 共 93 条，已列 2 条——提高 limit 至 ≥93，或收窄 path / 降 depth 可看全' }, 89);
  const card2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(card2.querySelector('.tn').textContent).toBe('📂 展开 CCBP · depth 2 · 2 条目（共 93 条）');
});

test('工具专属图标——find 🔍 / move 📦 / delete 🗑️ 覆盖骨架图标', () => {
  expect(window.chatToolIcon('file-find')).toBe('🔍');
  expect(window.chatToolIcon('file-move')).toBe('📦');
  expect(window.chatToolIcon('file-delete')).toBe('🗑️');
  // 未配专属图标的 text-* 仍吃骨架图标
  expect(window.chatToolIcon('text-read')).toBe('📖');
  expect(window.chatToolIcon('text-write')).toBe('📝');
  // 2026-09-29 补配——内置件与 VisionCat 新件脱离 📝 兜底
  expect(window.chatToolIcon('timeback')).toBe('⚓');
  expect(window.chatToolIcon('image-inject')).toBe('🖼️');
  expect(window.chatToolIcon('Note')).toBe('📌');
  expect(window.chatToolIcon('time')).toBe('🕒');
  expect(window.chatToolIcon('random')).toBe('🎲');
  expect(window.chatToolIcon('sleep')).toBe('⏳');
  expect(window.chatToolIcon('timer')).toBe('⏰');
  expect(window.chatToolIcon('pack')).toBe('📚');
});

test('image-inject——登记 text 骨架（不再落探测回落）；折叠行「已插入主干」+ 输入图片预览', () => {
  expect(window.chatToolSkeleton('image-inject')).toBe('text');
  const card = renderTool({
    name: 'image-inject',
    arguments: JSON.stringify({ path: 'C:/x/a.png' }),
    result: '{"ok":true,"tool":"image-inject","path":"C:/x/a.png"}'
  }, 201);
  expect(card.querySelector('.tn').textContent).toBe('🖼️ 已插入主干 · a.png');
  // 输入段前置图片预览（覆盖表 inputImages——被插入的图直接看得见）
  expect(card.querySelectorAll('.seg-img img').length).toBe(1);
});

test('段折叠摘要——摘要挂 summary 内（details 折叠时可见）', () => {
  const lines = [];
  for (let i = 1; i <= 8; i = i + 1) { lines.push('l' + i); }
  const card = renderTool({
    name: 'text-read',
    arguments: JSON.stringify({ path: 'a.txt' }),
    result: lines.join('\n')
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
    result: '{"ok":false,"tool":"cs-check","project":"X","files":3,"errors":2,"warnings":1}\nA.cs:3:1: CS1026: 缺少 )\nB.cs:9:5: CS1002: 需要 ;'
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
    result: '{"ok":true,"tool":"cs-read","file":"C:\\\\x\\\\A.cs","class":"A","member":"M","start":612,"end":614}\n        private void M() // L612\n        { // L613\n        } // L614'
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
  renderTool({ name: 'cs-build', arguments: JSON.stringify({ path: 'C:/a/B.csproj' }), result: '{"ok":true,"tool":"cs-build","project":"B","exit":0,"errors":0,"warnings":0,"ms":4600}' }, 94);
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
    result: '{"ok":false,"tool":"cs-check","project":"Bad","files":1,"errors":1,"warnings":0}\nBad.cs:3:19: CS1026: 应输入 )'
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
    result: '{"ok":true,"tool":"cs-find_ref","class":"X","member":"Y","hits":2,"projects":9}\nA.cs:228:55: payload["summary"] = Build(...)\n[跨程序集] B.cs:20:45: string s = Build("text-read", ...)'
  }, 104);
  expect(card.querySelector('.tn').textContent).toBe('🔍 引用 X.Y · 2 处 · 9 项目');
  const hits = card.querySelectorAll('.seg-out .seg-hit');
  expect(hits.length).toBe(2);
  expect(hits[0].querySelector('.seg-hit-path').textContent).toBe('A.cs');
  expect(hits[0].querySelector('.seg-hit-line').textContent).toBe(':228:55');
  expect(hits[1].querySelector('.seg-hit-cross')).not.toBeNull();
  expect(hits[1].querySelector('.seg-hit-line').textContent).toBe(':20:45');
});

test('cs-find——headline 命中数/项目数；声明行整行渲染；输入段含查找值', () => {
  const card = renderTool({
    name: 'cs-find',
    arguments: JSON.stringify({ path: 'Git:mau/Mau.sln', name: 'WorkspaceConfig' }),
    result: '{"ok":true,"tool":"cs-find","name":"WorkspaceConfig","hits":1,"projects":10}\n[Mau.Runtime] WorkspaceConfig.cs:12: 类 WorkspaceConfig'
  }, 130);
  expect(card.querySelector('.tn').textContent).toBe('🔍 查找 WorkspaceConfig · 1 处 · 10 项目');
  const hits = card.querySelectorAll('.seg-out .seg-hit');
  expect(hits.length).toBe(1);
  expect(hits[0].querySelector('.seg-hit-path').textContent).toBe('[Mau.Runtime] WorkspaceConfig.cs');
  expect(hits[0].querySelector('.seg-hit-line').textContent).toBe(':12');
  expect(hits[0].querySelector('.seg-hit-ctx').textContent).toBe(': 类 WorkspaceConfig');
  expect(card.textContent).toContain('查找声明 WorkspaceConfig');
});

test('cs-find——无命中（ERR，无结构化头）折叠行不空白', () => {
  const card = renderTool({
    name: 'cs-find',
    arguments: JSON.stringify({ path: 'Git:mau/Mau.sln', name: 'NoSuchSymbolXyz' }),
    result: 'ERR|SYMBOL_NOT_FOUND|未找到匹配的声明: NoSuchSymbolXyz'
  }, 131);
  expect(card.querySelector('.tn').textContent).toContain('查找 NoSuchSymbolXyz');
});

test('写类三件——outputLines 由 meta 生成自然语言；无正文不留白', () => {
  const card = renderTool({
    name: 'cs-member',
    arguments: JSON.stringify({ path: 'X.csproj', class: 'A', op: 'insert' }),
    result: '{"ok":true,"tool":"cs-member","op":"insert","class":"A","file":"A.cs","start":123,"end":140,"kind":"MethodDeclarationSyntax"}'
  }, 105);
  expect(card.querySelector('.tn').textContent).toBe('📝 插入 A · 落盘 L123-140');
  expect(card.querySelector('.seg-out .seg-line').textContent).toContain('已落盘 A.cs · L123-140');
  renderTool({ name: 'cs-comment', arguments: JSON.stringify({ path: 'X.csproj', class: 'A', member: 'M', type: 'summary' }), result: '{"ok":true,"tool":"cs-comment","class":"A","member":"M","type":"summary"}' }, 106);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('📝 注释 A.M · summary');
  expect(c2.querySelector('.seg-out .seg-line').textContent).toContain('已写入 A.M 的 summary 注释');
});

test('cs-member 批量 insert——count/items 驱动折叠行与落盘清单', () => {
  const card = renderTool({
    name: 'cs-member',
    arguments: JSON.stringify({ path: 'X.csproj', class: 'A', op: 'insert', position: 'end', codes: ['public int One() { return 1; }', 'public int Two() { return 2; }'] }),
    result: '{"ok":true,"tool":"cs-member","op":"insert","class":"A","file":"A.cs","count":2,"items":[{"start":123,"end":126,"kind":"MethodDeclarationSyntax"},{"start":128,"end":131,"kind":"MethodDeclarationSyntax"}]}'
  }, 129);
  expect(card.querySelector('.tn').textContent).toBe('📝 批量插入 2 个成员 · 落盘 L123-131');
  const lines = card.querySelectorAll('.seg-out .seg-line');
  expect(lines[0].textContent).toContain('#1 L123-126');
  expect(lines[1].textContent).toContain('#2 L128-131');
});

// ── A64 批 1：SearchCat / VisionCat / TempToolCat / Majordomo（结构化头驱动）──
test('批1——web-search 结构化头：引用数与协议入折叠行；正文剥头渲染', () => {
  const card = renderTool({
    name: 'web-search',
    arguments: JSON.stringify({ query: 'CH4 工具渲染' }),
    result: '{"ok":true,"tool":"web-search","query":"CH4 工具渲染","protocol":"anthropic","citations":2,"chars":14}\n答案正文第一行\n第二行'
  }, 107);
  expect(card.querySelector('.tn').textContent).toBe('🌐 联网搜索 CH4 工具渲染 · 2 条引用 · anthropic');
  const out = card.querySelector('.seg-out .tr');
  expect(out.textContent).toContain('答案正文第一行');
  expect(out.textContent.indexOf('"tool"')).toBe(-1);
});

test('批1——temp-info 结构化头：键值表由 meta 驱动 + 正文另起块；无注册显式', () => {
  const card = renderTool({
    name: 'temp-info',
    arguments: '{}',
    result: '{"ok":true,"tool":"temp-info","count":2,"keys":["demo.a","demo.b"]}\ndemo.a,demo.b'
  }, 108);
  expect(card.querySelector('.tn').textContent).toBe('🧩 临时工具 · 2 个可用 Key');
  expect(card.querySelectorAll('.seg-out .seg-kv').length).toBe(4);
  expect(card.querySelector('.seg-out .tr').textContent).toBe('demo.a,demo.b');
  renderTool({ name: 'temp-info', arguments: '{}', result: '{"ok":true,"tool":"temp-info","count":0,"keys":[]}' }, 109);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('🧩 临时工具 · 暂无注册');
});

test('批1——temp-exec / majordomo-restart 结构化头：exec 骨架剥头 + 输入意图行', () => {
  const card = renderTool({
    name: 'temp-exec',
    arguments: JSON.stringify({ key: 'demo.echo', content: 'hi' }),
    result: '{"ok":true,"tool":"temp-exec","key":"demo.echo","chars":2}\nhi'
  }, 110);
  expect(card.querySelector('.tn').textContent).toBe('💻 临时执行 demo.echo · 2 字');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('hi');
  expect(card.querySelector('.seg-in .seg-line').textContent).toContain('临时执行 demo.echo');
  renderTool({
    name: 'majordomo-restart',
    arguments: '{}',
    result: '{"ok":true,"tool":"majordomo-restart","target":"mauout","push":true}\n宿主重启请求已登记（目标运行区: mauout）。'
  }, 111);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('💻 宿主重启 · 目标 mauout · 带回执');
  expect(c2.querySelector('.seg-out .tr').textContent).toContain('宿主重启请求已登记');
});

test('批1——image-analyze 结构化头：text 骨架剥头 + 字数；参数行含提示词', () => {
  const card = renderTool({
    name: 'image-analyze',
    arguments: JSON.stringify({ path: 'MauOut/pet/idle.webp', question: '这是什么' }),
    result: '{"ok":true,"tool":"image-analyze","path":"MauOut/pet/idle.webp","question":"这是什么","chars":6}\n一只猫在睡觉'
  }, 112);
  expect(card.querySelector('.tn').textContent).toBe('📝 识别图片 idle.webp · 6 字');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('一只猫在睡觉');
  const inLines = card.querySelectorAll('.seg-in .seg-line');
  expect(inLines.length).toBe(2);
  expect(inLines[1].textContent).toContain('这是什么');
});

// ── A64 批 2：ConfigCat（config-* 4 件结构化头驱动）──
test('批2——config-list 结构化头：条数与可写数入折叠行', () => {
  const card = renderTool({
    name: 'config-list',
    arguments: '{}',
    result: '{"ok":true,"tool":"config-list","count":37,"writable":12,"sensitive":4}\nllm.endpoint= | 来源=default | 默认= | 可写 | 描述…'
  }, 113);
  expect(card.querySelector('.tn').textContent).toBe('📂 配置列表 · 37 项 · 12 可写');
});

test('批2——config-get 结构化头：来源与键值表由 meta 驱动', () => {
  const card = renderTool({
    name: 'config-get',
    arguments: JSON.stringify({ key: 'ui.port' }),
    result: '{"ok":true,"tool":"config-get","key":"ui.port","source":"default","declared":true,"writable":true,"sensitive":false}\nui.port=8080 | 来源=default | 默认=8080 | 可写 | 描述…'
  }, 114);
  expect(card.querySelector('.tn').textContent).toBe('🧩 读取配置 ui.port · 来源 default');
  expect(card.querySelectorAll('.seg-out .seg-kv').length).toBe(7);
});

test('批2——config-set / config-reset 结构化头：text 骨架剥头 + 意图行', () => {
  const card = renderTool({
    name: 'config-set',
    arguments: JSON.stringify({ key: 'ui.port', value: '8090' }),
    result: '{"ok":true,"tool":"config-set","key":"ui.port","sensitive":false}\n配置已更新: ui.port=8090'
  }, 115);
  expect(card.querySelector('.tn').textContent).toBe('📝 设置配置 ui.port · 已更新');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('配置已更新: ui.port=8090');
  renderTool({
    name: 'config-reset',
    arguments: '{}',
    result: '{"ok":true,"tool":"config-reset","key":"","scope":"all","count":12}\n已还原默认: 全部可写配置项'
  }, 116);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('📝 还原全部可写配置 · 12 项');
});

// ── A64 批 3：MauCat（mau-* 4 件结构化头驱动）──
test('批3——mau-verify 结构化头：通过时报告数、失败时错误数', () => {
  const c1 = renderTool({
    name: 'mau-verify',
    arguments: JSON.stringify({ file: 'corpus/ch4/TextCat/text_cat.mau' }),
    result: '{"ok":true,"tool":"mau-verify","file":"corpus/ch4/TextCat/text_cat.mau","flow":"FL_TextCat","reports":2}\nOK 验证通过: corpus/ch4/TextCat/text_cat.mau → FL_TextCat\n报告: 3 导线'
  }, 117);
  expect(c1.querySelector('.tn').textContent).toBe('🩺 Mau 验证 text_cat.mau · 通过（2 报告）');
  renderTool({
    name: 'mau-verify',
    arguments: JSON.stringify({ file: 'bad.mau' }),
    result: '{"ok":false,"tool":"mau-verify","file":"bad.mau","flow":"FL_bad","errors":2}\nFAIL|VALIDATE|bad.mau\nbad.mau:3: E201: 引用缺失'
  }, 118);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('⚠️ Mau 验证 bad.mau · 2 个错误');
  expect(c2.classList.contains('err')).toBe(true);
});

test('批3——mau-proj / mau-setup 结构化头：步数、编译标记、步通过数', () => {
  const c1 = renderTool({
    name: 'mau-proj',
    arguments: JSON.stringify({ proj: 'corpus/ch4/ConfigCat/config_cat.mauproj', build: true }),
    result: '{"ok":true,"tool":"mau-proj","proj":"ConfigCat","steps":4,"errors":0,"build":true}\n步骤 1/4 …\n构建成功: FL_ConfigCat.dll'
  }, 119);
  expect(c1.querySelector('.tn').textContent).toBe('🩺 组翻译 ConfigCat · 4 步 · 已编译');
  renderTool({
    name: 'mau-setup',
    arguments: JSON.stringify({ mode: 'prepare' }),
    result: '{"ok":true,"tool":"mau-setup","mode":"prepare","target":"","exit":0,"steps":7,"stepsOk":7,"artifacts":12}\nexit=0\nOk=true | 报告时间 …'
  }, 120);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('💻 一键部署 prepare · 7/7 步 · 12 产物');
});

test('批3——mau-gen 结构化头：不编译路径的步数', () => {
  const card = renderTool({
    name: 'mau-gen',
    arguments: JSON.stringify({ proj: 'corpus/ch4/MauCat/mau_cat.mauproj' }),
    result: '{"ok":true,"tool":"mau-gen","proj":"MauCat","steps":4,"errors":0,"build":false}\n步骤 1/4 …'
  }, 121);
  expect(card.querySelector('.tn').textContent).toBe('🩺 Mau 生成 MauCat · 4 步');
});

// ── A64 批 4：PsCat 结构化回执 + 内置 7 件 ──
test('批4——powershell 结构化头 + 正文：exit 徽标与 stdout / stderr 按行数切分', () => {
  const card = renderTool({
    name: 'powershell',
    arguments: JSON.stringify({ command: 'Get-Date' }),
    result: '{"ok":true,"tool":"powershell","exit":0,"truncated":false,"timeout":false,"stdoutLines":2,"stderrLines":1}\n第一行输出\n第二行输出\n错误信息'
  }, 122);
  const cap = card.querySelector('.seg-out .seg-cap').textContent;
  expect(cap).toContain('exit 0');
  const secCaps = card.querySelectorAll('.seg-out .seg-sec-cap');
  expect(secCaps.length).toBe(2);
  expect(secCaps[0].textContent).toContain('stdout');
  expect(secCaps[1].textContent).toContain('stderr');
  expect(card.querySelector('.seg-out .tr').textContent).toBe('第一行输出\n第二行输出');
});

test('A129——Note.content 原生数组：输入意图行显示条数 · 字符数', () => {
  const card = renderTool({
    name: 'Note',
    arguments: JSON.stringify({ action: 'set', content: ['任务A', '任务B', '任务C'] }),
    result: '{"ok":true,"tool":"Note","state":"progress","index":1,"total":3,"done":0,"remain":3,"last":false}\n[Note] 第1/3条  已完成0  待完成3\n任务目标：任务A'
  }, 200);
  expect(card.querySelector('.seg-in .seg-line').textContent).toBe('写入计划 · 3 条 · 9 字符');
});

test('批4——内置件结构化头：Note / time / random / host-flows / pack / host-reload', () => {
  const c1 = renderTool({
    name: 'Note',
    arguments: '{}',
    result: '{"ok":true,"tool":"Note","state":"progress","index":2,"total":3,"done":1,"remain":2,"last":false}\n[Note] 第2/3条  已完成1  待完成2\n任务目标：B'
  }, 123);
  expect(c1.querySelector('.tn').textContent).toBe('📌 任务追踪 · 第2/3条 · 已完成1 待完成2');
  renderTool({ name: 'time', arguments: '{}', result: '{"ok":true,"tool":"time","ts":"2026-09-18 19:00:00"}\n2026-09-18 19:00:00' }, 124);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toBe('🕒 时间 · 2026-09-18 19:00:00');
  renderTool({ name: 'random', arguments: JSON.stringify({ min: 1, max: 10 }), result: '{"ok":true,"tool":"random","min":1,"max":10,"value":7}\n7' }, 125);
  const c3 = chatMsgs.querySelectorAll('.chat-tool')[2];
  expect(c3.querySelector('.tn').textContent).toBe('🎲 随机数 [1,10) → 7');
  renderTool({ name: 'host-flows', arguments: '{}', result: '{"ok":true,"tool":"host-flows","count":10}\nFlow 运行现状（10 个）:' }, 126);
  const c4 = chatMsgs.querySelectorAll('.chat-tool')[3];
  expect(c4.querySelector('.tn').textContent).toBe('🧩 Flow 现状 · 10 个');
  renderTool({ name: 'pack', arguments: JSON.stringify({ key: 'overwork' }), result: '{"ok":true,"tool":"pack","key":"overwork","files":7,"chars":42000}\n✅ PACK overwork | 7 件 / 42000 字符' }, 127);
  const c5 = chatMsgs.querySelectorAll('.chat-tool')[4];
  expect(c5.querySelector('.tn').textContent).toBe('📚 加载包 overwork · 7 件');
  renderTool({ name: 'host-reload', arguments: JSON.stringify({ cat: 'TextCat' }), result: '{"ok":true,"tool":"host-reload","cat":"TextCat","oldId":9,"newId":17,"pid":2568}\nreload TextCat: #9 → #17 | pid=2568' }, 128);
  const c6 = chatMsgs.querySelectorAll('.chat-tool')[5];
  expect(c6.querySelector('.tn').textContent).toBe('💻 热重载 TextCat · #9 → #17');
});

// ── info：分类 JSON 块（整块解析——后端不再包「头 + 正文」两段）──
test('info——分类 JSON 块：折叠行取版本与猫；输出段按大类摊平成键值行', () => {
  const result = JSON.stringify({
    ok: true,
    tool: 'info',
    cat: 'cat-abc',
    version: { version: '1.03.017', build: '2026-09-18 20:00:00' },
    time: { now: '2026-09-18 21:00:00' },
    llm: { protocol: 'opencode', host: 'opencode.ai', model: 'deepseek-v4-flash', source: '猫绑定' },
    endpoint: { chat: 'http://127.0.0.1:8085', panel: 'http://127.0.0.1:8080' },
    roots: [{ id: 'WorkSpace', writable: true, note: '默认工作区域', path: 'C:/roots/WorkSpace' }, { id: 'Data', writable: false, note: '', path: 'C:/roots/Data' }],
    tokens: { context: 1234 },
    packs: [{ key: 'overwork', desc: '收工加载包' }],
    qqbot: { usage: 'Coder：发本地文件…' }
  }, null, 2);
  const card = renderTool({ name: 'info', arguments: '{}', result: result }, 130);
  expect(card.querySelector('.tn').textContent).toBe('🧭 环境信息 · v1.03.017 · 猫 cat-abc');
  expect(card.querySelectorAll('.seg-out .seg-kv').length).toBe(10);
  const out = card.querySelector('.seg-out').textContent;
  expect(out).toContain('http://127.0.0.1:8085');
  expect(out).toContain('http://127.0.0.1:8080');
  expect(out).toContain('WorkSpace(rw)[默认工作区域]');
  expect(out).toContain('WorkSpace → C:/roots/WorkSpace');
  expect(out).toContain('Data → C:/roots/Data');
  expect(out).toContain('1234 tokens');
  expect(out).toContain('overwork(收工加载包)');
});

test('info——非分类 JSON（provider 未注入）原文兜底，不静默空白', () => {
  const card = renderTool({
    name: 'info',
    arguments: '{}',
    result: 'ERR|INFO_NO_PROVIDER|环境信息不可用（未注入 provider）'
  }, 131);
  expect(card.querySelector('.tn').textContent).toContain('环境信息');
  expect(card.querySelector('.seg-out').textContent).toContain('INFO_NO_PROVIDER');
});

// ── catinfo：全猫状态统计（整块分类 JSON——同 info 规格）──
test('majordomo-catinfo——分类 JSON 块：折叠行取猫数；输出段每猫一行键值', () => {
  const result = JSON.stringify({
    ok: true,
    tool: 'cat.info',
    time: { now: '2026-09-24 14:10:00' },
    count: 2,
    cats: [
      {
        id: 'majordomo', name: 'majordomo', special: true, running: true, port: 8082,
        phase: 'Idle', runState: 'idle', requests: 0, round: 3, msgCount: 120,
        pending: 0, noteActive: false, contextCount: 42, context: 35127, lastActiveAt: 0
      },
      { id: 'cat-2', name: 'Coder', special: false, running: false, port: 0 }
    ]
  }, null, 2);
  const card = renderTool({ name: 'majordomo-catinfo', arguments: '{}', result: result }, 132);
  expect(card.querySelector('.tn').textContent).toBe('🐾 全猫状态 · 2 只猫');
  expect(card.querySelectorAll('.seg-out .seg-kv').length).toBe(2);
  const out = card.querySelector('.seg-out').textContent;
  expect(out).toContain('majordomo★');
  expect(out).toContain('运行中 :8082');
  expect(out).toContain('前文 35127 tokens / 42 条');
  expect(out).toContain('轮 3');
  expect(out).toContain('Coder');
  expect(out).toContain('静默');
});

test('majordomo-catinfo——非 JSON（服务未注入 / 指令未识别）原文兜底，不静默空白', () => {
  const card = renderTool({
    name: 'majordomo-catinfo',
    arguments: '{}',
    result: 'ERR|HOSTCMD_NO_SERVICE|宿主指令服务未注入（宿主未接线）'
  }, 133);
  expect(card.querySelector('.tn').textContent).toContain('全猫状态');
  expect(card.querySelector('.seg-out').textContent).toContain('HOSTCMD_NO_SERVICE');
});

// ── 覆盖缺口补齐（2026-09-28 · Z8 前置）——file-version / sleep / timer / config-cat-* ──
test('折叠行——file-version 从正文「版本:」行取版本（编译时刻留在正文）', () => {
  const body = '路径: app.dll\n版本: 1.03.039+2026-09-28 11:09:09\nFileVersion: 1.3.39.0\nProductVersion: 1.03.039+2026-09-28 11:09:09\n修改时间: 2026-09-28 11:09:09\n大小: 12345 字节';
  const t = window.chatToolHeadline({ name: 'file-version', arguments: '{"path":"app.dll"}', result: body });
  expect(t).toBe('版本信息 app.dll · 1.03.039');
});

test('折叠行——sleep 从结构化头取时长与到点时刻', () => {
  const due = new Date(2026, 8, 28, 12, 30, 0).getTime();
  const result = '{"ok":true,"tool":"sleep","hours":0,"minutes":5,"seconds":0,"dueAt":' + due + '}\n已登记定时唤醒。';
  const t = window.chatToolHeadline({ name: 'sleep', arguments: '{"minutes":5}', result: result });
  expect(t).toBe('定时唤醒 · 5 分 · 到点 12:30:00');
});

test('折叠行——timer 标注循环 + 到点时刻；骨架落位 text', () => {
  const due = new Date(2026, 8, 28, 13, 0, 0).getTime();
  const result = '{"ok":true,"tool":"timer","hours":1,"minutes":0,"seconds":0,"loop":true,"dueAt":' + due + '}\n已登记定时注入。';
  const t = window.chatToolHeadline({ name: 'timer', arguments: '{"content":"巡检","hours":1,"loop":true}', result: result });
  expect(t).toBe('定时注入 · 1 时 · 循环 · 到点 13:00:00');
  expect(window.chatToolSkeleton('timer')).toBe('text');
});

test('骨架落位——config-cat-get / config-cat-set 登记 text · file-version 登记 listing', () => {
  expect(window.chatToolSkeleton('config-cat-get')).toBe('text');
  expect(window.chatToolSkeleton('config-cat-set')).toBe('text');
  expect(window.chatToolSkeleton('file-version')).toBe('listing');
  expect(window.chatToolSkeleton('sleep')).toBe('text');
});

// ── Z8 折叠行兜底（2026-09-28）——宿主 summary 退役后的两层保障 ──
test('折叠行覆盖哨兵——有骨架的工具必配 headline（PS 双线豁免）', () => {
  const exempt = ['powershell', 'powershell7'];
  const missing = [];
  const names = Object.keys(window.CHAT_TOOL_SKELETONS);
  for (let i = 0; i < names.length; i = i + 1) {
    const n = names[i];
    if (exempt.indexOf(n) >= 0) { continue; }
    const ov = window.CHAT_TOOL_OVERRIDES[n];
    if (!ov || typeof ov.headline !== 'function') { missing.push(n); }
  }
  expect(missing).toEqual([]);
});

test('骨架兜底折叠行——「骨架名 · 工具名」；未登记骨架回落工具名；空名回落 ?', () => {
  expect(window.chatToolFallbackHeadline({ name: 'powershell' })).toBe('命令执行 · powershell');
  expect(window.chatToolFallbackHeadline({ name: 'text-read' })).toBe('读取文件 · text-read');
  expect(window.chatToolFallbackHeadline({ name: 'brand-new-tool' })).toBe('brand-new-tool');
  expect(window.chatToolFallbackHeadline({})).toBe('?');
});

test('timeback——折叠行带 purpose（结构化头驱动；start / back 两侧）', () => {
  const c1 = renderTool({
    name: 'timeback',
    arguments: JSON.stringify({ action: 'start', purpose: 'A125 探索' }),
    result: '{"ok":true,"tool":"timeback","id":13,"anchor":287,"purpose":"A125 探索"}\ntimeback #13 已锚定（锚点 = 本次调用声明 · 节点 287 · 用途「A125 探索」）——查证过程留在作用域内。'
  }, 132);
  expect(c1.querySelector('.tn').textContent).toContain('TimeBack #13 已锚定 · 起点 287');
  expect(c1.querySelector('.tn').textContent).toContain('A125 探索');
  renderTool({
    name: 'timeback',
    arguments: JSON.stringify({ action: 'back', findings: '结论：X' }),
    result: '{"ok":true,"tool":"timeback","id":13,"anchor":287,"purpose":"A125 探索","released":17,"tokens":297884,"grew":6120}\n结论：X'
  }, 133);
  const c2 = chatMsgs.querySelectorAll('.chat-tool')[1];
  expect(c2.querySelector('.tn').textContent).toContain('TimeBack #13 已登记回收 · 锚点 287');
  expect(c2.querySelector('.tn').textContent).toContain('A125 探索');
});

test('未登记工具折叠行——骨架兜底不空白（Z8）', () => {
  const card = renderTool({ name: 'brand-new-tool', arguments: '{}', result: 'OK' }, 200);
  expect(card.querySelector('.tn').textContent).toContain('brand-new-tool');
});

// ── A127 执行序徽标（折叠行；宿主 order 表裁决）──
test('A127——工具卡折叠行渲染 order 徽标（载荷带 order 才渲染）', () => {
  const det = renderTool({ name: 'text-read', arguments: '{}', result: 'ok', toolIndex: 1, toolTotal: 1, order: '-1' }, 61);
  const badge = det.querySelector('summary .chat-order');
  expect(badge).not.toBeNull();
  expect(badge.textContent).toBe('⚙-1');
});

test('A127——载荷缺 order 字段不渲染徽标（旧块 / 未带——零猜测）', () => {
  const det = renderTool({ name: 'text-read', arguments: '{}', result: 'ok', toolIndex: 1, toolTotal: 1 }, 62);
  expect(det.querySelector('summary .chat-order')).toBeNull();
});
