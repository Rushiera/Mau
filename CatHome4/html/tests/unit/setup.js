// tests/unit/setup.js —— 测试环境初始化（node 环境 + 手动 JSDOM）
// 定位：加载 chat.html 独立对话页骨架到全局 DOM（F2.4 门禁迁移——对话逻辑唯一真相源 = js/chat-core.js + js/chat-note.js）+ mock 浏览器 API（EventSource/fetch）
// 前端是原生 JS 全局函数——测试用 vm.runInThisContext 加载脚本，函数挂 globalThis 经 window 别名断言
// 主面板（index.html / app.js / panel.js）用例：smoke.test.mjs 自建 index.html JSDOM（主面板纯管理面——F2.1）
// DOM 装配与浏览器 API mock → tests/unit/mock-env.js（P20-P3-17 公共件）
import { readFileSync } from 'node:fs';
import { installDom, installMockEventSource, installMockFetch, installMockClipboard } from './mock-env.js';

// 1. 解析 chat.html（前端独立对话页骨架——测试环境与生产同构）+ 安装全局 DOM
const html = readFileSync(new URL('../../chat.html', import.meta.url), 'utf-8');
installDom(html);

// 2. mock 浏览器 API（SSE / HTTP / 剪贴板——测试环境无真实连接）
installMockEventSource();
installMockFetch();
installMockClipboard();
