// tests/unit/chat-delay.test.mjs —— 延迟指令队列外观层纯函数测试（chat-delay.js）
// 覆盖：倒计时文本 · 到点时刻文本 · 来源标记 · 副列文本 · 即将触发判据 · 快捷档（定义 + 相对换算）· datetime-local 解析与回填 · 指令行拼装 · chips 出口
// A83（2026-09-23）：自由文本时长解析退役（改档位 chips + datetime-local），原 4 例随之出表
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

beforeAll(async () => {
  const src = await readFile(new URL('../../js/chat-delay.js', import.meta.url), 'utf-8');
  vm.runInThisContext(src, { filename: 'chat-delay.js' });
});

// ── 倒计时文本 ──
test('倒计时文本——分:秒 / 时:分:秒 / 天+时', () => {
  expect(delayFmtRemain(65000)).toBe('1:05');
  expect(delayFmtRemain(3661000)).toBe('1:01:01');
  expect(delayFmtRemain(90000000)).toBe('1天1时');
  expect(delayFmtRemain(-5000)).toBe('0:00');
});

// ── 到点时刻文本——今天只显时刻 ──
test('到点时刻文本——今天为 HH:mm:ss', () => {
  const d = new Date();
  d.setHours(23, 5, 7, 0);
  expect(delayFmtDue(d.getTime())).toBe('23:05:07');
});

// ── 来源标记 ──
test('来源标记——delay/sleep/restart/timer 四态', () => {
  expect(delaySrcMark('delay')).toBe('⏰');
  expect(delaySrcMark('sleep')).toBe('💤');
  expect(delaySrcMark('restart')).toBe('🔄');
  expect(delaySrcMark('timer')).toBe('⏳');
  expect(delaySrcMark('')).toBe('⏰');
});

// ── 副列文本——循环与已响标记 ──
test('副列文本——循环/已响标记组合', () => {
  const d = new Date();
  d.setHours(23, 5, 7, 0);
  const dueAt = d.getTime();
  expect(delayDueText({ source: 'timer', dueAt: dueAt, loop: false, fired: 0 })).toBe('23:05:07');
  expect(delayDueText({ source: 'timer', dueAt: dueAt, loop: true, fired: 0 })).toBe('23:05:07 🔁');
  expect(delayDueText({ source: 'timer', dueAt: dueAt, loop: true, fired: 3 })).toBe('23:05:07 🔁 已响 3');
  expect(delayDueText({ source: 'delay', dueAt: dueAt, loop: false, fired: 2 })).toBe('23:05:07 已响 2');
});

// ── 即将触发判据 ──
test('即将触发判据——≤60 秒为真', () => {
  expect(delayCdSoon(60001)).toBe(false);
  expect(delayCdSoon(60000)).toBe(true);
  expect(delayCdSoon(0)).toBe(true);
  expect(delayCdSoon(-1)).toBe(true);
});

// ── 快捷档定义与相对换算 ──
test('快捷档——四档定义与相对换算（同刻取值零漂移）', () => {
  expect(delayQuickDefs.map((d) => d.label)).toEqual(['5m', '15m', '30m', '1h']);
  expect(delayQuickDefs.map((d) => d.sec)).toEqual([300, 900, 1800, 3600]);
  const now = 1700000000000;
  expect(delayQuickDueAt(300, now) - now).toBe(300 * 1000);
  expect(delayQuickDueAt(3600, now) - now).toBe(3600 * 1000);
});

// ── datetime-local 解析 ──
test('datetime-local 解析——带秒/不带秒/非法/空', () => {
  expect(delayParseLocalValue('')).toBeNull();
  expect(delayParseLocalValue('   ')).toBeNull();
  expect(delayParseLocalValue('abc')).toBeNull();
  expect(delayParseLocalValue('2026-09-23')).toBeNull();
  const full = delayParseLocalValue('2027-01-02T03:04:05');
  const d = new Date(full);
  expect(d.getFullYear()).toBe(2027);
  expect(d.getMonth()).toBe(0);
  expect(d.getDate()).toBe(2);
  expect(d.getHours()).toBe(3);
  expect(d.getMinutes()).toBe(4);
  expect(d.getSeconds()).toBe(5);
  expect(delayParseLocalValue('2027-01-02T03:04')).toBe(full - 5000);
});

// ── datetime-local 回填往返 ──
test('datetime-local 回填——往返一致', () => {
  expect(delayFmtLocalValue(delayParseLocalValue('2027-01-02T03:04:05'))).toBe('2027-01-02T03:04:05');
  const t = delayQuickDueAt(1800, 1700000000000);
  expect(delayParseLocalValue(delayFmtLocalValue(t))).toBe(t);
});

// ── 指令行拼装 ──
test('指令行拼装——相对/绝对/循环/改时刻/取消/循环切换', () => {
  expect(delayAddLine(300, '看看进度')).toBe('delay.add|300|看看进度');
  expect(delayAddAtLine(1700000000000, '内容 带空格')).toBe('delay.addat|1700000000000|内容 带空格');
  expect(delayAddAtLoopLine(1700000000000, '巡检')).toBe('delay.addatloop|1700000000000|巡检');
  expect(delaySetLine(7, 1700000000000)).toBe('delay.set|7|1700000000000');
  expect(delayCancelLine(7)).toBe('delay.cancel|7');
  expect(delayLoopLine(7, true)).toBe('delay.loop|7|1');
  expect(delayLoopLine(7, false)).toBe('delay.loop|7|0');
});

// ── chips 出口——新建区与编辑条共用 ──
test('chips 出口——新建区（带自定义）/ 编辑条（带 id）', () => {
  const add = delayChipsHtml('quick-pick', null, '<button class="delay-chip" id="delayChipCustom" data-act="custom-toggle">自定义</button>');
  expect(add).toContain('data-act="quick-pick"');
  expect(add).toContain('data-sec="300"');
  expect(add).not.toContain('data-id');
  expect(add).toContain('data-act="custom-toggle"');
  const edit = delayChipsHtml('edit-quick', 12, null);
  expect(edit).toContain('data-act="edit-quick"');
  expect(edit).toContain('data-id="12"');
  expect(edit).not.toContain('custom-toggle');
});
