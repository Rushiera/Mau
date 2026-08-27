// tests/unit/setup.js —— 测试环境初始化（node 环境 + 手动 JSDOM）
// 定位：加载 index.html 真实页面骨架到全局 DOM + mock 浏览器 API（EventSource/fetch）
// 前端是原生 JS 全局函数——测试用 new Function(code).call(window) 加载脚本，函数挂 window 上断言
import { readFileSync } from 'node:fs';
import { JSDOM } from 'jsdom';

// 1. 解析 index.html（前端真实页面骨架——测试环境与生产同构）
const html = readFileSync(new URL('../../index.html', import.meta.url), 'utf-8');
const dom = new JSDOM(html, { url: 'http://127.0.0.1:8080', runScripts: 'outside-only' });

// 2. 全局 window 指向 globalThis（前端 var/function 声明经 vm.runInThisContext 挂 globalThis——window 别名可访问）
//    document 指向真实页面 DOM（navigator 在 Node 22+ 只读 getter——defineProperty 覆盖）
globalThis.window = globalThis;
globalThis.document = dom.window.document;
Object.defineProperty(globalThis, 'navigator', { value: dom.window.navigator, configurable: true });
globalThis.Node = dom.window.Node;
globalThis.Event = dom.window.Event;
globalThis.HTMLElement = dom.window.HTMLElement;

// 3. mock EventSource（SSE——测试环境无真实连接；触发事件由测试手动调用）
class MockEventSource {
  constructor(url) { this.url = url; this._listeners = {}; }
  addEventListener(type, fn) { this._listeners[type] = fn; }
  close() {}
  // 测试辅助——手动触发事件
  fire(type, data) {
    const fn = this._listeners[type];
    if (fn) { fn({ data: JSON.stringify(data) }); }
  }
}
globalThis.EventSource = MockEventSource;

// 4. mock fetch（避免真实 HTTP 请求；默认返回空——测试按需 stub）
globalThis.fetch = async () => ({ json: async () => ({}), ok: true });
