// tests/unit/sse-seq.test.mjs —— SSE 帧序号落差检测单元测试（design-ch4-push-perf §3.2 · A140）
// 覆盖：连续无落差 · 跳号报缺失条数 · 首帧建基线 · 缺/非法 lastEventId 不误报 · 重连重置 · 累计
// 加载方式：setup.js 装配骨架到全局 DOM；本文件加载 js/ui-common.js 取纯函数 sseSeqTrack / sseSeqReset
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll, beforeEach } from 'vitest';

beforeAll(async () => {
  const src = await readFile(new URL('../../js/ui-common.js', import.meta.url), 'utf-8');
  vm.runInThisContext(src, { filename: 'ui-common.js' });
});

beforeEach(() => {
  sseSeqReset();
  globalThis.sseGapTotal = 0;
});

function mkEvent(id) {
  if (id === undefined || id === null) { return {}; }
  return { lastEventId: String(id) };
}

test('连续帧序号——无落差', () => {
  expect(sseSeqTrack(mkEvent(1))).toBe(0);
  expect(sseSeqTrack(mkEvent(2))).toBe(0);
  expect(sseSeqTrack(mkEvent(3))).toBe(0);
  expect(sseGapTotal).toBe(0);
});

test('跳号——返回缺失条数并累计', () => {
  sseSeqTrack(mkEvent(1));
  expect(sseSeqTrack(mkEvent(5))).toBe(3);
  expect(sseGapTotal).toBe(3);
  expect(sseSeqTrack(mkEvent(6))).toBe(0);
  expect(sseGapTotal).toBe(3);
});

test('首帧无基线——建基线不报落差', () => {
  expect(sseSeqTrack(mkEvent(7))).toBe(0);
  expect(sseGapTotal).toBe(0);
});

test('缺 lastEventId / 非数值——不报落差且不改基线', () => {
  sseSeqTrack(mkEvent(4));
  expect(sseSeqTrack({})).toBe(0);
  expect(sseSeqTrack({ lastEventId: 'abc' })).toBe(0);
  expect(sseSeqTrack(mkEvent(5))).toBe(0);
  expect(sseGapTotal).toBe(0);
});

test('重置——重连后新序列从 1 起不误报', () => {
  sseSeqTrack(mkEvent(100));
  sseSeqReset();
  expect(sseSeqTrack(mkEvent(1))).toBe(0);
  expect(sseSeqTrack(mkEvent(2))).toBe(0);
  expect(sseGapTotal).toBe(0);
});

test('累计——多次落差相加', () => {
  sseSeqTrack(mkEvent(1));
  sseSeqTrack(mkEvent(3));
  sseSeqTrack(mkEvent(6));
  expect(sseGapTotal).toBe(3);
});
