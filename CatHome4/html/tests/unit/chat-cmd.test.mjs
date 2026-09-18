// tests/unit/chat-cmd.test.mjs —— PowerShell 命令解读器单元测试（chat-cmd.js 纯函数）
// 覆盖：单段/多段切分 + 引号感知 + 管道标记 + 宿主 CLI 子指令解码 + git 带值开关 + 未识别标注 + 参数截断兜底 + 重定向上报
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

beforeAll(async () => {
  const src = await readFile(new URL('../../js/chat-cmd.js', import.meta.url), 'utf-8');
  vm.runInThisContext(src, { filename: 'chat-cmd.js' });
});

function args(command) {
  return JSON.stringify({ command: command });
}

// ── 单段：编译链 ──
test('单段 dotnet build——折叠行与逐段解读', () => {
  const r = cmdDecodeTool(args('dotnet build CatHome4.sln'));
  expect(r.brief).toBe('编译 C# 项目 「CatHome4.sln」');
  expect(r.detail).toBe('🔎 命令意图（1 段）\n 1. 编译 C# 项目 「CatHome4.sln」');
  expect(r.truncated).toBe(false);
});

// ── 多段：分号切分 + 管道标记 ──
test('多段切分 + 管道标记', () => {
  const r = cmdDecodeTool(args('git status; Get-ChildItem -Recurse Flows | Select-Object -First 5'));
  expect(r.brief).toBe('查看仓库状态 等 3 段');
  expect(r.detail).toContain(' 1. 查看仓库状态');
  expect(r.detail).toContain(' 2. 列出目录 「Flows」');
  expect(r.detail).toContain(' 3. ⤷ 取字段/条数');
});

// ── 宿主 CLI——子指令解码（部署/重载类可见性关键面）──
test('宿主 CLI --run 子指令解码（reload）', () => {
  const r = cmdDecodeTool(JSON.stringify({ command: '& CatHome4.exe --run "reload TextCat"' }));
  expect(r.brief).toBe('宿主 CLI 执行指令「热重载 Flow TextCat」');
});

test('宿主 CLI --tool-check 与 --selfcheck', () => {
  expect(cmdDecodeTool(args('CatHome4.exe --tool-check text-read {"path":"a"}')).brief).toBe('工具全链自检 「text-read」');
  expect(cmdDecodeTool(args('CatHome4.exe --selfcheck')).brief).toBe('宿主启动自检');
});

// ── 一键部署链 / Mau 基座 CLI ──
test('SetUp 模式识别', () => {
  expect(cmdDecodeTool(args('SetUp.exe prepare --report r.json')).brief).toBe('一键部署链·就地自举（prepare）');
  expect(cmdDecodeTool(args('SetUp.exe deploy C:\\out')).brief).toBe('一键部署链·发布到目标目录（deploy）');
});

test('Mau 组翻译命令解读', () => {
  const r = cmdDecodeTool(args('Mau-public\\Mau.exe proj corpus\\ch4\\PsCat\\ps_cat.mauproj -o public\\src\\PsCat --build'));
  expect(r.brief).toContain('Mau 组翻译+编译');
});

// ── 版本控制：带值开关在动作之前 ──
test('git 带值开关跳过——动作在开关之后', () => {
  const r = cmdDecodeTool(args('git -C C:\\repo status'));
  expect(r.brief).toBe('查看仓库状态');
});

test('git 子命令映射 + 目标参数', () => {
  expect(cmdDecodeTool(args('git log -1 --oneline')).brief).toBe('查看提交历史');
  expect(cmdDecodeTool(args('git add L1/Tree.md')).brief).toBe('暂存改动 「L1/Tree.md」');
});

// ── 引号感知 ──
test('引号内分号不分段', () => {
  const r = cmdDecodeTool(args('Get-Content "a;b.txt"'));
  expect(r.brief).toBe('读取文件内容 「a;b.txt」');
});

// ── 重定向上报（写文件语义——历史命令可能含）──
test('重定向上报为写文件', () => {
  const r = cmdDecodeTool(args('Get-ChildItem > out.txt'));
  expect(r.brief).toBe('列出目录（结果写入文件 「out.txt」）');
});

// ── 未识别段——显式标注（不编造、不静默）──
test('未识别段显式标注', () => {
  const r = cmdDecodeTool(args('Whatever-Func -Foo bar'));
  expect(r.brief).toBe('未识别命令');
  expect(r.detail).toContain('❓ 未识别：Whatever-Func -Foo bar');
});

// ── 参数截断兜底（宿主实时 payload 截 200 字，JSON 未闭合）──
test('参数截断兜底——JSON 未闭合仍可解读', () => {
  const r = cmdDecodeTool('{"command":"git status; Get-ChildItem -Recurse');
  expect(r.truncated).toBe(true);
  expect(r.brief).toBe('查看仓库状态 等 2 段（截断）');
  expect(r.detail).toContain('参数被宿主截断');
});

// ── 兜底：无法解读返回 null（调用方回退宿主 summary）──
test('无 command 字段返回 null', () => {
  expect(cmdDecodeTool('{"path":"a.txt"}')).toBe(null);
  expect(cmdDecodeTool('')).toBe(null);
  expect(cmdDecodeTool(null)).toBe(null);
  expect(cmdDecodeTool('{"command":"   "}')).toBe(null);
});

// ── 覆盖率采集——未识别段上报（cmd-unknown 持久化）──
test('未识别段产出待上报项（token 小写归一 + raw 原文 + 样本）', () => {
  const r = cmdDecodeTool(args('Get-CimInstance Win32_Process'));
  expect(r.unknown.length).toBe(1);
  expect(r.unknown[0].token).toBe('get-ciminstance');
  expect(r.unknown[0].raw).toBe('Get-CimInstance');
  expect(r.unknown[0].sample).toBe('Get-CimInstance Win32_Process');
});

test('已识别段不产出待上报项；混合命令只收未识别段', () => {
  expect(cmdDecodeTool(args('git status')).unknown.length).toBe(0);
  const r = cmdDecodeTool(args('git status; Get-CimInstance Win32_Process'));
  expect(r.unknown.length).toBe(1);
  expect(r.unknown[0].token).toBe('get-ciminstance');
});

test('token 归一——路径型取末段文件名；空段返回空串', () => {
  expect(cmdUnknownToken('"C:\\tools\\Custom.exe" --flag')).toBe('custom.exe');
  expect(cmdUnknownToken('Whatever-Func -Foo')).toBe('whatever-func');
  expect(cmdUnknownToken('')).toBe('');
});

test('上报去重——同 token 本页只报一次（fetch 载荷核销）', () => {
  const calls = [];
  globalThis.fetch = function (url, opt) {
    calls.push({ url: url, body: JSON.parse(opt.body) });
    return Promise.resolve({ ok: true });
  };
  globalThis.CMD_UNKNOWN_REPORTED = {};
  cmdReportUnknown([{ token: 'get-ciminstance', sample: 'a' }]);
  cmdReportUnknown([{ token: 'get-ciminstance', sample: 'b' }, { token: 'x-y', sample: 'c' }]);
  expect(calls.length).toBe(2);
  expect(calls[0].url).toBe('/api/v1/cmd-unknown');
  expect(calls[0].body.items.length).toBe(1);
  expect(calls[1].body.items.length).toBe(1);
  expect(calls[1].body.items[0].token).toBe('x-y');
});

test('上报失败释放标记——下次渲染可重试（不静默丢弃采集机会）', () => {
  const calls = [];
  globalThis.fetch = function (url, opt) {
    calls.push(JSON.parse(opt.body));
    return Promise.resolve({ ok: false });
  };
  globalThis.CMD_UNKNOWN_REPORTED = {};
  cmdReportUnknown([{ token: 'retry-me', raw: 'Retry-Me', sample: 'Retry-Me' }]);
  return Promise.resolve().then(() => Promise.resolve()).then(() => {
    expect(calls.length).toBe(1);
    expect(globalThis.CMD_UNKNOWN_REPORTED['retry-me']).toBe(undefined);
  });
});
