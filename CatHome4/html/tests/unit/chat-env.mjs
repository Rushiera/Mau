// ═══════════════════════════════════════════
// tests/unit/chat-env.mjs —— 对话页测试基座（A182 新主干回归网）
//
// 定位：把 chat.html 的**真实脚本清单**装配进一个 JSDOM 环境——测试环境与生产同构
//       （design-ch4-frontend-test §四·一：setup.js 解析真实页面骨架，非 mock 页面）。
//
// 🔴 关键性质：脚本清单**取自 chat.html 正本**，不手抄——清单漂移（加件 / 改名 / 顺序错）
//    当场被装配用例暴露；新增块型 / fx 件自动进入本基座。
//
// 边界：本件只负责「装配 + 清场 + fetch 捕获」，不含断言；引导层（inline script）不执行——
//       `chatBoot()` / `fxBoot()` 由用例显式调用（测试自行掌控启动时机）。
// ═══════════════════════════════════════════

import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { installDom, installMockEventSource, installMockFetch, installMockClipboard } from './mock-env.js';

const HTML_URL = new URL('../../chat.html', import.meta.url);

/**
 * 解析 chat.html 的外链脚本清单（按出现顺序）
 * @param {string} html chat.html 全文
 * @returns {string[]} 脚本相对路径（已剥离 `?v=` 缓存参数）
 */
export function chatScriptSrcs(html) {
    const out = [];
    const re = /<script\s+src="([^"]+)"/g;
    let m;
    while ((m = re.exec(html)) !== null) {
        out.push(m[1].split('?')[0]);
    }
    return out;
}

/**
 * 装配对话页——JSDOM 骨架 + 按 chat.html 清单顺序加载全部外链脚本到全局
 * @returns {Promise<{html: string, srcs: string[]}>} html 全文与脚本清单
 */
export async function bootChatPage() {
    const html = await readFile(HTML_URL, 'utf-8');
    installDom(html);
    installMockEventSource();
    installMockFetch();
    // 桌宠素材链消费 `r.blob()`（mock-env 默认只给 json）——补 blob 让素材在 jsdom 内走通，
    // 不制造 14 条「素材加载失败」噪声（那是环境限制非产品缺陷，噪声会掩真信号）
    globalThis.fetch = async () => ({
        ok: true,
        status: 200,
        json: async () => ({}),
        blob: async () => new Blob([])
    });
    installMockClipboard();
    const srcs = chatScriptSrcs(html);
    for (const src of srcs) {
        const url = new URL('../../' + src, import.meta.url);
        const code = await readFile(url, 'utf-8');
        vm.runInThisContext(code, { filename: url.toString() });
    }
    return { html: html, srcs: srcs };
}

/** 清场——清空持久区 / 临时区 / 插话队列（用例间隔离） */
export function clearAreas() {
    const msgs = document.getElementById('chatMsgs');
    if (msgs) {
        msgs.textContent = '';
    }
    const live = document.getElementById('chatLivePanel');
    if (live) {
        live.textContent = '';
    }
    if (typeof window.pendingClear === 'function') {
        window.pendingClear();
    }
    // 切换态复位——新块驱动的自动切换会改输入区 / 流式区显隐（每例从输入态起，隔离用例间影响）
    if (typeof window.chatLiveShow === 'function') {
        window.chatLiveShow(false);
    }
    // 流式态色复位（态色标在输入区容器上，归零到 empty——同属用例间隔离）
    const zone = document.getElementById('chatinput');
    if (zone) {
        zone.setAttribute('data-live', 'empty');
    }
}

/**
 * 捕获 fetch 调用——出口面断言用（记录 url 与 option）
 * @returns {Array<{url: string, opt: object}>} 调用记录（按序追加）
 */
export function captureFetch() {
    const calls = [];
    globalThis.fetch = async (url, opt) => {
        calls.push({ url: url, opt: opt });
        return { json: async () => ({ ok: true }) };
    };
    return calls;
}

/**
 * 等待微任务队列排空——fetch 链（then 两级）落地用
 * @returns {Promise<void>} 微任务一轮
 */
export function flushMicro() {
    return new Promise((resolve) => setTimeout(resolve, 0));
}
