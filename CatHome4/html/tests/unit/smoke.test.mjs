// tests/unit/smoke.test.mjs —— 工具链冒烟测试（验证 Vitest + jsdom + 脚本加载链路可用）
// F2.4 门禁迁移——主面板（index.html / app.js / panel.js）用例保留；对话用例迁至 chat-view.test.mjs（chat.html 独立页）
import { readFile } from 'node:fs/promises';
import { JSDOM } from 'jsdom';
import vm from 'node:vm';
import { expect, test, beforeAll } from 'vitest';

// 加载无导出脚本到全局（vm.runInThisContext——var/function 声明挂 globalThis，经 window 别名访问）
async function runGlobalScript(url) {
  const code = await readFile(url, 'utf-8');
  vm.runInThisContext(code, { filename: url.toString() });
}

beforeAll(async () => {
  // F2.1 主面板纯管理面——index.html 不再加载 chat.js（对话迁 chat.html 独立页）
  const html = await readFile(new URL('../../index.html', import.meta.url), 'utf-8');
  const dom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });
  globalThis.window = globalThis;
  globalThis.document = dom.window.document;
  Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
  globalThis.Node = dom.window.Node;
  globalThis.Event = dom.window.Event;
  globalThis.HTMLElement = dom.window.HTMLElement;
  class MockEventSource {
    constructor(url) { this.url = url; this._listeners = {}; }
    addEventListener(type, fn) { this._listeners[type] = fn; }
    close() {}
  }
  globalThis.EventSource = MockEventSource;
  globalThis.fetch = async () => ({ json: async () => ({}), ok: true });
  // 主面板四域（app 公共 → panel 多猫 → panel-apis API/QQBot 池 → panel-catcfg 每猫配置/受控根——2026-09-08 拆分）
  await runGlobalScript(new URL('../../js/app.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-apis.js', import.meta.url));
  await runGlobalScript(new URL('../../js/panel-catcfg.js', import.meta.url));
});

test('index.html 主面板骨架已加载（meta 元素存在 + 无对话区 DOM）', () => {
  expect(document.getElementById('meta')).not.toBeNull();
  expect(document.getElementById('sessions')).not.toBeNull();
  // F2.1 前端唯一化——主面板纯管理面，无对话区 DOM
  expect(document.getElementById('tab-chat')).toBeNull();
  expect(document.getElementById('chatMsgs')).toBeNull();
});

test('app.js 全局函数已挂载', () => {
  expect(typeof window.applySnapshot).toBe('function');
  expect(typeof window.renderCats).toBe('function');
  expect(typeof window.renderBoxes).toBe('function');
});

test('panel.js 多猫管理函数已挂载（special 渲染支持——F2.2）', () => {
  expect(typeof window.loadCats).toBe('function');
  expect(typeof window.renderCatRow).toBe('function');
  expect(typeof window.catAction).toBe('function');
});

test('panel 拆分——API/QQBot 池 + 每猫配置函数已挂载（2026-09-08 体量治理）', () => {
  expect(typeof window.loadApis).toBe('function');
  expect(typeof window.apiSubmit).toBe('function');
  expect(typeof window.loadQqBots).toBe('function');
  expect(typeof window.qqBotSubmit).toBe('function');
  expect(typeof window.openCatCfg).toBe('function');
  expect(typeof window.loadRoots).toBe('function');
  expect(typeof window.renderGroupedChecks).toBe('function');
});

test('对话逻辑迁至 chat.html——app.js 无旧阶段模型分派', () => {
  expect(typeof window.chatOnLlm).toBe('undefined');
  expect(typeof window.chatSend).toBe('undefined');
});
