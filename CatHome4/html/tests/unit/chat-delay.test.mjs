// tests/unit/chat-delay.test.mjs —— 延迟指令队列外观层纯函数测试（chat-delay.js）
// 覆盖：时长/时刻解析（相对 · 当日时刻 · 绝对）· 倒计时文本 · 到点时刻文本 · 来源标记
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

beforeAll(async () => {
  const src = await readFile(new URL('../../js/chat-delay.js', import.meta.url), 'utf-8');
  vm.runInThisContext(src, { filename: 'chat-delay.js' });
});

// ── 相对时长解析 ──
test('相对时长——秒/分/时与组合写法', () => {
  const now = Date.now();
  const s90 = delayParseInput('90s');
  expect(s90 - now).toBeGreaterThan(89000);
  expect(s90 - now).toBeLessThan(92000);
  const m10 = delayParseInput('10m');
  expect(m10 - now).toBeGreaterThan(599000);
  expect(m10 - now).toBeLessThan(602000);
  const h = delayParseInput('1h30m');
  expect(h - now).toBeGreaterThan(5399000);
  expect(h - now).toBeLessThan(5402000);
});

// ── 当日时刻——已过则顺延明天 ──
test('当日时刻 HH:mm——返回未来时刻且时分正确', () => {
  const ts = delayParseInput('09:30');
  expect(ts).toBeGreaterThan(Date.now());
  const d = new Date(ts);
  expect(d.getHours()).toBe(9);
  expect(d.getMinutes()).toBe(30);
  expect(d.getSeconds()).toBe(0);
});

// ── 绝对时刻 ──
test('绝对时刻 yyyy-MM-dd HH:mm:ss——按本机时区解析', () => {
  const ts = delayParseInput('2027-01-02 03:04:05');
  expect(ts).not.toBeNull();
  const d = new Date(ts);
  expect(d.getFullYear()).toBe(2027);
  expect(d.getMonth()).toBe(0);
  expect(d.getDate()).toBe(2);
  expect(d.getHours()).toBe(3);
  expect(d.getMinutes()).toBe(4);
  expect(d.getSeconds()).toBe(5);
});

// ── 非法输入 ──
test('非法输入——空串/无单位文本返回 null', () => {
  expect(delayParseInput('')).toBeNull();
  expect(delayParseInput('   ')).toBeNull();
  expect(delayParseInput('abc')).toBeNull();
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

// ── 行首文本——循环与已响标记 ──
test('行首文本——循环/已响标记组合', () => {
  const d = new Date();
  d.setHours(23, 5, 7, 0);
  const base = { source: 'timer', dueAt: d.getTime(), loop: false, fired: 0 };
  expect(delayWhenText(base)).toBe('⏳ 23:05:07');
  expect(delayWhenText({ source: 'timer', dueAt: d.getTime(), loop: true, fired: 0 })).toBe('⏳ 23:05:07 🔁');
  expect(delayWhenText({ source: 'timer', dueAt: d.getTime(), loop: true, fired: 3 })).toBe('⏳ 23:05:07 🔁 已响 3');
  expect(delayWhenText({ source: 'delay', dueAt: d.getTime(), loop: false, fired: 2 })).toBe('⏰ 23:05:07 已响 2');
});
