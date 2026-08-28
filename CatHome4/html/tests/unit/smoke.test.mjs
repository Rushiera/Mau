// tests/unit/smoke.test.mjs —— 工具链冒烟测试（验证 Vitest + jsdom + 脚本加载链路可用）
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

// 加载无导出脚本到全局（vm.runInThisContext——var/function 声明挂 globalThis，经 window 别名访问）
async function runGlobalScript(url) {
  const code = await readFile(url, 'utf-8');
  vm.runInThisContext(code, { filename: url.toString() });
}

beforeAll(async () => {
  // 加载 app.js（定义全局状态与渲染函数）+ chat.js（对话逻辑）
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/chat.js', import.meta.url));
});

test('index.html 骨架已加载（meta 元素存在）', () => {
  expect(document.getElementById('meta')).not.toBeNull();
  expect(document.getElementById('chatMsgs')).not.toBeNull();
});

test('app.js 全局函数已挂载', () => {
  expect(typeof window.applySnapshot).toBe('function');
  expect(typeof window.renderCats).toBe('function');
});

test('chat.js 全局函数已挂载', () => {
  expect(typeof window.chatOnLlm).toBe('function');
  expect(typeof window.chatSend).toBe('function');
  expect(typeof window.chatBubble).toBe('function');
});
