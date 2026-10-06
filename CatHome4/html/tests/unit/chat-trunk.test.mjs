// ═══════════════════════════════════════════
// tests/unit/chat-trunk.test.mjs —— 对话页主干回归网（A182）
//
// 覆盖（设计 §三 组 1 判据「前端测试覆盖主干收包三段 + 各 fx 件」的主干侧）：
//   ① 装配——chat.html 脚本清单全量加载 + 主干入口就位 + 订阅面（含 A181 回归哨兵）
//   ② 收包三段——全量 / 追加 / 变化增量（契约 §12.2 / §12.3）
//   ③ 兜底与隔离——未登记 type / 单块异常 / 坏 JSON（失败可见，不静默丢块）
//   ④ 跨件契约——user 块到达 → 插话队列出列
//   ⑤ 出口面——发送（`Chat ` 前缀）/ 停止 / 继续 / 新会话 / 临时区切换
//   ⑥ 按钮态派生（fx/controls）
//
// 边界：**只测契约入口的对外行为**，不测块内载荷语义（逐 type 载荷核对归 A198）——
//       实现细节重构（如 A193/A194 的 state 分脚本）不得使本网变红。
// ═══════════════════════════════════════════

import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { bootChatPage, clearAreas, captureFetch, flushMicro } from './chat-env.mjs';

let srcs = [];

beforeAll(async () => {
    const r = await bootChatPage();
    srcs = r.srcs;
});

beforeEach(() => {
    clearAreas();
});

// ── ① 装配 ──────────────────────────────────
describe('装配——脚本清单与入口', () => {
    it('chat.html 清单全量加载（清单取自正本，非手抄）', () => {
        expect(srcs.length).toBeGreaterThan(20);
        expect(srcs).toContain('js/chat/main.js');
        expect(srcs).toContain('js/chat/fx/registry.js');
        expect(srcs).toContain('js/chat/blocks/user.js');
    });

    it('主干契约入口全部就位', () => {
        const names = [
            'chatBoot', 'chatOnFrame', 'chatParseFrame', 'persistApply', 'liveApply', 'stateApply',
            'fxBoot', 'postCommand', 'chatSend', 'chatPause', 'chatContinue', 'chatNewSession'
        ];
        const missing = names.filter((n) => typeof window[n] !== 'function');
        expect(missing).toEqual([]);
    });

    it('声明面三表就位且键集一致（registry ↔ 块声明）', () => {
        const decl = Object.keys(window.BLOCK_DECL);
        const persist = Object.keys(window.PERSIST_RENDERERS);
        const live = Object.keys(window.LIVE_RENDERERS);
        // A196——toolrun 由 live.js 逐卡复用工具卡渲染件（不入 LIVE_RENDERERS）；empty 不入块声明表（不产元素）
        expect(persist.concat(live, ['toolrun']).sort()).toEqual(decl.slice().sort());
        expect(window.RUN_PHASES.length).toBe(6);
        expect(window.FX_FEATURES.length).toBe(6);
    });
});

// ── 引导 / 订阅面 ────────────────────────────
describe('引导——单流 + 订阅面（契约 §12.1）', () => {
    it('chatBoot 建立单一 SSE 连接', () => {
        window.chatBoot();
        expect(window.chatSse).toBeTruthy();
        expect(window.chatSse.url).toContain('/api/v1/stream');
    });

    it('订阅面只含 chat 面事件（A181 + A191 回归哨兵：管理面事件与旧 note 事件不得入 chat 流）', () => {
        const q = window.chatSse.url.split('topics=')[1] || '';
        const topics = q.split(',');
        expect(topics).toContain('view');
        expect(topics).not.toContain('cmd');
        expect(topics).not.toContain('note');
        expect(topics).not.toContain('patch');
        expect(topics).not.toContain('snapshot');
        expect(topics).not.toContain('log');
    });

    it('fxBoot 五件入口全部可解析（无「未启动」告警）', () => {
        const warned = [];
        const orig = console.warn;
        console.warn = function () { warned.push(Array.prototype.join.call(arguments, ' ')); };
        try {
            window.fxBoot();
        } finally {
            console.warn = orig;
        }
        const unresolved = warned.filter((w) => w.indexOf('独立功能未启动') >= 0);
        expect(unresolved).toEqual([]);
    });
});

// ── ② 收包三段 ───────────────────────────────
describe('收包三段（契约 §12.2 / §12.3）', () => {
    it('全量帧——persist 清区重绘 + live 全量镜像 + state 投影', () => {
        const box = document.getElementById('chatMsgs');
        box.innerHTML = '<div class="stale">旧内容</div>';
        window.chatOnFrame({
            v: 2,
            state: { sessionId: 'cat1', runState: 'think', runMs: { think: 1500 }, requests: 0, tokens: { count: 3 }, meta: { displayName: 'cat1' } },
            persist: { mode: 'full', items: [{ type: 'user', payload: { text: '你好' } }] },
            live: { type: 'replysse', context: '流式中' }
        });
        expect(box.querySelector('.stale')).toBeNull();
        expect(box.querySelectorAll('.chat-row').length).toBe(1);
        expect(document.getElementById('chatLivePanel').querySelectorAll('.chat-row').length).toBe(1);
        // A201 改写：信息位 = 前文条数与长度 / 前文字符数 / 会话级消耗（原显示猫 key 的 sessionId 位已弃）
        expect(document.getElementById('chatInfo').textContent).toContain('前文 3 条');
        expect(document.title).toBe('cat1');
    });

    it('追加帧——persist append 逐条挂载（不清理既有块）', () => {
        const box = document.getElementById('chatMsgs');
        window.persistApply({ mode: 'append', items: [{ type: 'user', payload: { text: 'A' } }] });
        window.persistApply({ mode: 'append', items: [{ type: 'text', payload: { text: 'B' } }] });
        expect(box.querySelectorAll('.chat-row').length).toBe(2);
    });

    it('变化增量帧——live 段整体替换（状态投影：type + context 两字符串）', () => {
        const panel = document.getElementById('chatLivePanel');
        window.liveApply({ type: 'replysse', context: '一' });
        expect(panel.querySelectorAll('.chat-row').length).toBe(1);
        window.liveApply({ type: 'thinksse', context: '二' });
        expect(panel.querySelectorAll('.chat-row').length).toBe(1);
        expect(panel.textContent).toContain('二');
        // toolrun——context 为未完成工具卡数组 JSON：逐卡复用持久区渲染件
        window.liveApply({ type: 'toolrun', context: '[{"name":"time","arguments":"{}","toolIndex":1,"toolTotal":2},{"name":"random","arguments":"{}","toolIndex":2,"toolTotal":2}]' });
        expect(panel.querySelectorAll('.chat-row').length).toBe(2);
        // empty——链路正常、内容为空：不产元素
        window.liveApply({ type: 'empty', context: '' });
        expect(panel.querySelectorAll('.chat-row').length).toBe(0);
    });

    it('三段各归各位——互不串区', () => {
        window.chatOnFrame({
            state: { runState: 'idle' },
            persist: { mode: 'append', items: [{ type: 'user', payload: { text: 'P' } }] },
            live: { type: 'thinksse', context: 'L' }
        });
        expect(document.getElementById('chatMsgs').textContent).toContain('P');
        expect(document.getElementById('chatMsgs').textContent).not.toContain('L');
        expect(document.getElementById('chatLivePanel').textContent).toContain('L');
    });

    it('state 段 delay 投影到定时面板（A185 回归哨兵：旁路端点退役）', async () => {
        const calls = captureFetch();
        window.stateApply({
            delay: { entries: [{ id: 7, content: '测试', dueAt: Date.now() + 60000, createdAt: 1, source: 'delay', loop: false, intervalMs: 0, fired: 0 }] }
        });
        await flushMicro();
        expect(window.delayEntries.length).toBe(1);
        expect(calls.filter((c) => String(c.url).indexOf('/api/v1/delay') >= 0).length).toBe(0);
    });

    it('state 段 conn 投影到状态条（A186——服务端健康 / 多页连接）', () => {
        window.stateApply({ conn: { server: 'ok', clients: 2 } });
        expect(document.getElementById('chatStatus').textContent).toContain('👥 2');
        window.stateApply({ conn: { server: 'stopping', clients: 1 } });
        expect(document.getElementById('chatStatus').textContent).toContain('重启中');
        window.stateApply({ conn: { server: 'ok', clients: 1 } });
        expect(document.getElementById('chatConn')).toBeNull();
    });
});

// ── ③ 兜底与隔离 ─────────────────────────────
describe('兜底与隔离（失败必须可见）', () => {
    it('未登记 type → 兜底块（块不消失，显式出声）', () => {
        window.persistApply({ mode: 'append', items: [{ type: 'nope.unknown', payload: { a: 1 } }] });
        const box = document.getElementById('chatMsgs');
        expect(box.querySelectorAll('.chat-row').length).toBe(1);
        expect(box.textContent).toContain('未识别的块型');
        expect(box.textContent).toContain('nope.unknown');
    });

    it('单块渲染异常 → 错误块，其余块照常（功能隔离）', () => {
        const orig = window.PERSIST_RENDERERS['text'];
        window.PERSIST_RENDERERS['text'] = function () { throw new Error('boom'); };
        try {
            window.persistApply({
                mode: 'append',
                items: [
                    { type: 'user', payload: { text: '前' } },
                    { type: 'text', payload: { text: '中间抛错' } },
                    { type: 'user', payload: { text: '后' } }
                ]
            });
        } finally {
            window.PERSIST_RENDERERS['text'] = orig;
        }
        const box = document.getElementById('chatMsgs');
        expect(box.querySelectorAll('.chat-row').length).toBe(3);
        expect(box.textContent).toContain('块渲染失败');
        expect(box.textContent).toContain('前');
        expect(box.textContent).toContain('后');
    });

    it('坏 JSON → 解析返回 null（不抛、不中断连接）', () => {
        expect(window.chatParseFrame('{oops')).toBeNull();
        expect(window.chatParseFrame('{"state":{"runState":"idle"}}')).not.toBeNull();
    });
});

// ── ④ 跨件契约 ───────────────────────────────
describe('跨件契约——插话队列（fx/pending）', () => {
    it('入列于发送时刻；user 块到达即出列', () => {
        window.pendingAdd('在途消息');
        expect(document.querySelectorAll('#chatPendingPanel > div').length).toBe(1);
        window.persistApply({ mode: 'append', items: [{ type: 'user', payload: { text: '在途消息' } }] });
        expect(document.querySelectorAll('#chatPendingPanel > div').length).toBe(0);
    });

    it('全量重绘即清队（本地在途记录失去意义）', () => {
        window.pendingAdd('x');
        window.persistApply({ mode: 'full', items: [] });
        expect(document.querySelectorAll('#chatPendingPanel > div').length).toBe(0);
    });
});

// ── ⑤ 出口面 ─────────────────────────────────
describe('出口面（input.js——投递即回执）', () => {
    it('空文本零动作（不发请求）', async () => {
        const calls = captureFetch();
        document.getElementById('chatSendInput').value = '   ';
        window.chatSend();
        await flushMicro();
        expect(calls.length).toBe(0);
    });

    it('发送——带 `Chat ` 前缀 + 清空输入框', async () => {
        const calls = captureFetch();
        const box = document.getElementById('chatSendInput');
        box.value = '你好';
        window.chatSend();
        await flushMicro();
        expect(calls.length).toBe(1);
        expect(JSON.parse(calls[0].opt.body).text).toBe('Chat 你好');
        expect(box.value).toBe('');
    });

    it('投递未受理 → 清队（防幽灵队列）', async () => {
        globalThis.fetch = async () => ({ json: async () => ({ ok: false, error: '未识别' }) });
        window.pendingAdd('在途');
        document.getElementById('chatSendInput').value = '在途';
        window.chatSend();
        await flushMicro();
        expect(document.querySelectorAll('#chatPendingPanel > div').length).toBe(0);
    });

    it('停止 / 继续 / 新会话——指令串固定', async () => {
        window.confirm = () => true;   // 新会话二次确认（2026-10-06）——本用例只验指令串
        const calls = captureFetch();
        window.chatPause();
        window.chatContinue();
        window.chatNewSession();
        await flushMicro();
        expect(calls.map((c) => JSON.parse(c.opt.body).text)).toEqual(['cat.pause', 'cat.continue', 'session.new']);
    });

    it('新会话——二次确认（取消不投递 · 2026-10-06）', async () => {
        window.confirm = () => false;
        const calls = captureFetch();
        window.chatNewSession();
        await flushMicro();
        expect(calls.length).toBe(0);
        window.confirm = () => true;
        window.chatNewSession();
        await flushMicro();
        expect(calls.length).toBe(1);
        expect(JSON.parse(calls[0].opt.body).text).toBe('session.new');
    });

    it('临时区切换——输入框与面板同区域互斥 + 面板高度固定（对话区不受影响）', () => {
        const panel = document.getElementById('chatLivePanel');
        window.chatLiveToggle();
        expect(panel.style.display).toBe('');
        expect(document.getElementById('chatSendInput').style.display).toBe('none');
        expect(document.getElementById('chatMsgs').style.display).toBe('');
        // 高度哨兵——固定高度随切换写入（jsdom 下 offsetHeight = 0，断言「写入行为」而非数值）
        expect(panel.style.height).not.toBe('');
        window.chatLiveToggle();
        expect(panel.style.display).toBe('none');
        expect(document.getElementById('chatSendInput').style.display).toBe('');
    });

    it('切换开关——页签直达该态（点「输入」回输入区 / 点「流式」去流式区）', () => {
        const sw = document.getElementById('chatLiveToggle');
        const panel = document.getElementById('chatLivePanel');
        const input = document.getElementById('chatSendInput');
        window.chatLiveShow(true);
        expect(panel.style.display).toBe('');
        expect(input.style.display).toBe('none');
        expect(sw.classList.contains('on')).toBe(true);
        window.chatLiveShow(true);
        expect(sw.classList.contains('on')).toBe(true);
        window.chatLiveShow(false);
        expect(panel.style.display).toBe('none');
        expect(input.style.display).toBe('');
        expect(sw.classList.contains('on')).toBe(false);
    });

    it('流式态标记——输入区 data-live 随 live 段 type（态色单一色源，映射归 CSS 令牌）', () => {
        const zone = document.getElementById('chatinput');
        window.liveApply({ type: 'toolrun', context: '[]' });
        expect(zone.getAttribute('data-live')).toBe('toolrun');
        window.liveApply({ type: 'thinksse', context: '想' });
        expect(zone.getAttribute('data-live')).toBe('thinksse');
        window.liveApply({ type: 'empty', context: '' });
        expect(zone.getAttribute('data-live')).toBe('empty');
    });

    it('自动切换——user 块到达切流式 / roundsum 到达切回输入（判据取持久区新块）', () => {
        const sw = document.getElementById('chatLiveToggle');
        const panel = document.getElementById('chatLivePanel');
        const input = document.getElementById('chatSendInput');
        window.chatLiveShow(false);
        window.persistApply({ mode: 'append', items: [{ type: 'user', payload: { text: '你好' } }] });
        expect(sw.classList.contains('on')).toBe(true);
        expect(panel.style.display).toBe('');
        expect(input.style.display).toBe('none');
        window.persistApply({ mode: 'append', items: [{ type: 'roundsum', payload: {} }] });
        expect(sw.classList.contains('on')).toBe(false);
        expect(input.style.display).toBe('');
    });

    it('自动切换——full 重放不触发（首帧 / 重连不跳态）', () => {
        const sw = document.getElementById('chatLiveToggle');
        window.chatLiveShow(false);
        window.persistApply({ mode: 'full', items: [{ type: 'user', payload: { text: '旧消息' } }] });
        expect(sw.classList.contains('on')).toBe(false);
    });
});

// ── ⑥ 按钮态派生 ─────────────────────────────
describe('按钮态派生（fx/controls——外观层推算）', () => {
    it('idle 与运行态派生相反', () => {
        const pause = document.getElementById('chatPause');
        const cont = document.getElementById('chatContinue');
        const noteStart = document.getElementById('noteStartBtn');
        window.stateApply({ runState: 'idle' });
        expect(pause.disabled).toBe(true);
        expect(cont.disabled).toBe(false);
        expect(noteStart.disabled).toBe(false);
        window.stateApply({ runState: 'think' });
        expect(pause.disabled).toBe(false);
        expect(cont.disabled).toBe(true);
        expect(noteStart.disabled).toBe(true);
        expect(document.getElementById('chatSendBtn').disabled).toBe(false);
    });

    it('状态条投影六态（当前态高亮）', () => {
        window.stateApply({ runState: 'tool', runMs: { link: 100, tool: 700 }, requests: 3 });
        const html = document.getElementById('chatStatus').innerHTML;
        expect(html).toContain('Tool');
        expect(html).toContain('active');
        expect(html).toContain('Api 3');
    });
});
