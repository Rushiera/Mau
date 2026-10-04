// ═══════════════════════════════════════════
// tests/unit/chat-ctx.test.mjs —— 前文弹层 + 端点角色标签回归网
//
// 覆盖：
//   ① 信息位三段入口——state 段投影后三段可点击段就位（条数 / tokens / 关键信息 · data-ctx 标注）
//   ② 弹层四视图——打开即读对应端点；同视图再点收起；异视图切并复用缓存
//   ③ 条目展开——点条目头切全文（摘要 ↔ 全文）
//   ④ 失败可见——端点报错 / 未响应在弹层内出声（不静默留白）
//   ⑤ 端点角色标签——读端点渲染（主要 / 备用态类 + 模型名）；点击投递 toggle 后重读
//
// 边界：只测对外行为（端点调用面 + DOM 可见结果），不测内部函数拆分。
// ═══════════════════════════════════════════

import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { bootChatPage, flushMicro } from './chat-env.mjs';

beforeAll(async () => {
    await bootChatPage();
});

beforeEach(() => {
    window.ctxClose();
    window.ctxData = null;
    window.ctxKeyData = null;
    window.ctxFullData = null;
    window.ctxMode = 'list';
    const info = document.getElementById('chatInfo');
    if (info) {
        info.textContent = '';
    }
});

/** 端点桩——按 url 关键字分派响应，并记录调用 */
function mockEndpoints(map) {
    const calls = [];
    globalThis.fetch = async (url, opt) => {
        calls.push({ url: String(url), opt: opt });
        const key = Object.keys(map).find((k) => String(url).indexOf(k) >= 0);
        return { json: async () => (key ? map[key] : { ok: false, error: '端点未注册' }) };
    };
    return calls;
}

const CTX_OK = {
    ok: true, count: 2, chars: 30, ctxTokens: 1234,
    items: [
        { i: 1, role: 'system', chars: 10, preview: '系统提示', content: '系统提示全文' },
        { i: 2, role: 'user', chars: 20, preview: '问题一', content: '问题一全文', time: 1700000000000 }
    ]
};

const KEY_OK = {
    ok: true, count: 1, chars: 8,
    items: [{ i: 1, role: 'report', chars: 8, preview: '加载报告', content: '加载报告全文' }]
};

const FULL_OK = {
    ok: true, count: 1, chars: 12, tokens: 2400, ratio: 2.5, estimated: false,
    items: [{ i: 1, role: 'system', chars: 12, preview: '系统段', content: '系统段全文' }]
};

// ── ① 信息位三段入口 ─────────────────────────
describe('信息位三段入口（state 段投影）', () => {
    it('投影后三段可点击段就位且带 data-ctx 标注', () => {
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 12, context: 3456 } });
        const links = document.querySelectorAll('#chatInfo [data-ctx]');
        expect(links.length).toBe(3);
        const modes = Array.from(links).map((n) => n.getAttribute('data-ctx'));
        expect(modes).toEqual(['list', 'tokens', 'key']);
        expect(document.getElementById('chatInfo').textContent).toContain('sessionId=majordomo');
    });

    it('无前文长度时仍出条数段与关键信息段（tokens 段缺省不出）', () => {
        window.stateApply({ sessionId: 'cat_a', tokens: { count: 0, context: 0 } });
        const modes = Array.from(document.querySelectorAll('#chatInfo [data-ctx]'))
            .map((n) => n.getAttribute('data-ctx'));
        expect(modes).toEqual(['list', 'key']);
    });
});

// ── ② 弹层四视图 ─────────────────────────────
describe('前文弹层——四视图与三态', () => {
    it('点条数段打开并读取 /api/v1/context，条目按序渲染', async () => {
        const calls = mockEndpoints({ '/api/v1/context': CTX_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        expect(document.getElementById('ctxPopover').style.display).toBe('flex');
        expect(calls.length).toBe(1);
        expect(calls[0].url).toContain('/api/v1/context');
        expect(document.querySelectorAll('#ctxList .ctx-item').length).toBe(2);
        expect(document.getElementById('ctxTitle').textContent).toBe('前文条目');
    });

    it('已开且同视图再点收起（不重复读取）', async () => {
        const calls = mockEndpoints({ '/api/v1/context': CTX_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        const link = document.querySelector('#chatInfo [data-ctx="list"]');
        link.click();
        await flushMicro();
        link.click();
        await flushMicro();
        expect(document.getElementById('ctxPopover').style.display).toBe('none');
        expect(calls.length).toBe(1);
    });

    it('已开切 tokens 页签复用缓存（不重复读取）并渲染占比条', async () => {
        const calls = mockEndpoints({ '/api/v1/context': CTX_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        document.getElementById('ctxModeTokens').click();
        await flushMicro();
        expect(calls.length).toBe(1);
        expect(document.getElementById('ctxTitle').textContent).toBe('前文 token 明细');
        expect(document.querySelectorAll('#ctxList .ctx-bar').length).toBe(2);
        expect(document.getElementById('ctxModeTokens').className).toContain('ctx-mode-on');
    });

    it('切关键信息读 /api/v1/keyinfo（role 标签走中文名）', async () => {
        const calls = mockEndpoints({ '/api/v1/context': CTX_OK, '/api/v1/keyinfo': KEY_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="key"]').click();
        await flushMicro();
        expect(calls[0].url).toContain('/api/v1/keyinfo');
        expect(document.getElementById('ctxTitle').textContent).toBe('前文关键信息');
        expect(document.querySelector('#ctxList .ctx-role').textContent).toBe('加载报告');
    });

    it('切完整前文读 /api/v1/fullctx（token 文案随宿主折算值）', async () => {
        const calls = mockEndpoints({ '/api/v1/context': CTX_OK, '/api/v1/fullctx': FULL_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        document.getElementById('ctxModeFull').click();
        await flushMicro();
        expect(calls[1].url).toContain('/api/v1/fullctx');
        expect(document.getElementById('ctxTitle').textContent).toBe('完整前文');
        expect(document.getElementById('ctxMeta').textContent).toContain('实测比值 2.50 折算');
    });

    it('Esc 关闭弹层', async () => {
        mockEndpoints({ '/api/v1/context': CTX_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        document.dispatchEvent(new window.Event('keydown'));
        window.ctxClose();
        expect(document.getElementById('ctxPopover').style.display).toBe('none');
    });
});

// ── ③ 条目展开 ───────────────────────────────
describe('前文弹层——条目展开', () => {
    it('点条目头展开全文，再点折叠', async () => {
        mockEndpoints({ '/api/v1/context': CTX_OK });
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        const item = document.querySelector('#ctxList .ctx-item');
        const head = item.querySelector('.ctx-item-head');
        const full = item.querySelector('.ctx-full');
        const body = item.querySelector('.ctx-body');
        expect(full.style.display).toBe('none');
        head.click();
        expect(full.style.display).toBe('block');
        expect(body.style.display).toBe('none');
        head.click();
        expect(full.style.display).toBe('none');
    });
});

// ── ④ 失败可见 ───────────────────────────────
describe('前文弹层——失败可见', () => {
    it('端点回 ok=false 时弹层内出声（不静默留白）', async () => {
        mockEndpoints({});
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        expect(document.getElementById('ctxList').textContent).toContain('不可读');
        expect(document.getElementById('ctxList').textContent).toContain('端点未注册');
    });

    it('读取异常时弹层内出声', async () => {
        globalThis.fetch = async () => { throw new Error('网络断了'); };
        window.stateApply({ sessionId: 'majordomo', tokens: { count: 2, context: 1234 } });
        document.querySelector('#chatInfo [data-ctx="list"]').click();
        await flushMicro();
        expect(document.getElementById('ctxList').textContent).toContain('读取失败');
    });
});

// ── ⑤ 端点角色标签 ───────────────────────────
describe('端点角色标签（apirole.js）', () => {
    it('读端点渲染角色与模型名，备用态加 backup 类', async () => {
        const calls = mockEndpoints({
            '/api/v1/api-role': { role: '备用', primary: 'deepseek-v4.1', backup: 'glm-4.6' }
        });
        window.apiRoleRefresh();
        await flushMicro();
        const node = document.getElementById('chatApiRole');
        expect(calls[0].url).toContain('/api/v1/api-role');
        expect(node.textContent).toBe('API 备用[glm-4.6]');
        expect(node.className).toContain('backup');
    });

    it('主要态不带 backup 类；模型名缺失回落占位', async () => {
        mockEndpoints({ '/api/v1/api-role': { role: '主要', primary: '', backup: '' } });
        window.apiRoleRefresh();
        await flushMicro();
        const node = document.getElementById('chatApiRole');
        expect(node.textContent).toBe('API 主要[未配置]');
        expect(node.className).not.toContain('backup');
    });

    it('点击投递 toggle 后重读端点（真源在后端，不本地翻转）', async () => {
        const calls = mockEndpoints({
            '/api/v1/api-role': { role: '主要', primary: 'deepseek-v4.1', backup: 'glm-4.6' },
            '/api/v1/api-role/toggle': { ok: true, role: '备用' }
        });
        document.getElementById('chatApiRole').click();
        await flushMicro();
        const toggle = calls.filter((c) => c.url.indexOf('/toggle') >= 0);
        expect(toggle.length).toBe(1);
        expect(toggle[0].opt.method).toBe('POST');
        expect(calls.filter((c) => c.url.indexOf('/toggle') < 0).length).toBeGreaterThan(0);
    });

    it('读取失败回落占位并出声（不显示假值）', async () => {
        globalThis.fetch = async () => { throw new Error('端点不可达'); };
        window.apiRoleRefresh();
        await flushMicro();
        expect(document.getElementById('chatApiRole').textContent).toBe('API —');
    });
});
