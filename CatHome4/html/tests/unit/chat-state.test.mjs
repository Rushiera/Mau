// ═══════════════════════════════════════════
// tests/unit/chat-state.test.mjs —— state 段投影回归网（A193）
//
// 覆盖组 3 判据（design-ch4-frontend-purify §三 组 3）：
//   ① 分发表为唯一映射处——字段面覆盖契约 §12.2 ① 八字段 · 表行形态完整 · 入口全可解析
//   ② 分发——按表逐行调用（顺序 = 表序）
//   ③ 失败可见——入口缺失出声 + 其余照常 · 单件异常隔离
//   ④ state.js 退化为薄层——整段覆盖（缺省字段回落默认）+ 字段面完整
//   ⑤ 接线哨兵——分发表在 chat.html 脚本清单内（清单漂移当场暴露）
//
// 边界：不测各投影件的 DOM 细节（那归 chat-trunk 对外行为断言与 A198 逐 type 核对）——
//       本件守「表 ↔ 分发 ↔ 薄层」三层契约；A194 实现迁移不得使其变红。
// ═══════════════════════════════════════════

import { beforeAll, afterEach, describe, expect, it } from 'vitest';
import { bootChatPage } from './chat-env.mjs';

/// 契约 §12.2 ① 字段面（后端 `ChatSession.BuildStateJson` 八字段）
const STATE_FIELDS = ['sessionId', 'runState', 'runMs', 'requests', 'note', 'delay', 'conn', 'tokens'];

/// 字段面派生——分发表各行 `fields` 的并集（与产品侧同口径：表是唯一声明处）
function fieldKeys(rows) {
    const out = [];
    for (const r of rows) {
        for (const f of r.fields) {
            if (out.indexOf(f) < 0) {
                out.push(f);
            }
        }
    }
    return out;
}

let srcs = [];
const stubbed = [];

beforeAll(async () => {
    const r = await bootChatPage();
    srcs = r.srcs;
});

afterEach(() => {
    for (const s of stubbed) {
        window[s.name] = s.orig;
    }
    stubbed.length = 0;
});

/// 以记录调用参数的桩替换投影入口——返回本条桩的调用记录
function stubApply(name) {
    const calls = [];
    stubbed.push({ name: name, orig: window[name] });
    window[name] = function (st) { calls.push(st); };
    return calls;
}

/// 捕获 console.warn——失败可见断言用
function captureWarn(fn) {
    const warned = [];
    const orig = console.warn;
    console.warn = function () { warned.push(Array.prototype.join.call(arguments, ' ')); };
    try {
        fn();
    } finally {
        console.warn = orig;
    }
    return warned;
}

describe('state 分发表（A193 骨架）', () => {
    it('字段面覆盖契约八字段（表内 fields 并集）', () => {
        expect(fieldKeys(window.STATE_DECL).slice().sort()).toEqual(STATE_FIELDS.slice().sort());
    });

    it('表行形态完整且入口全部可解析', () => {
        const rows = window.STATE_DECL;
        expect(rows.length).toBeGreaterThan(0);
        const bad = rows.filter((r) => typeof r.id !== 'string'
            || typeof r.name !== 'string'
            || (r.owner !== 'state' && r.owner !== 'fx')
            || !Array.isArray(r.fields) || r.fields.length === 0
            || typeof r.output !== 'string' || r.output.length === 0
            || typeof r.apply !== 'string');
        expect(bad).toEqual([]);
        const missing = rows.filter((r) => typeof window[r.apply] !== 'function');
        expect(missing.map((r) => r.id)).toEqual([]);
    });

    it('分发按表逐行调用一次（顺序 = 表序）', () => {
        const order = [];
        for (const r of window.STATE_DECL) {
            stubbed.push({ name: r.apply, orig: window[r.apply] });
            const id = r.id;
            window[r.apply] = function () { order.push(id); };
        }
        window.stateApply({ runState: 'idle' });
        expect(order).toEqual(window.STATE_DECL.map((r) => r.id));
    });

    it('入口缺失 → 出声且其余照常（失败必须可见，不静默跳过）', () => {
        const row = window.STATE_DECL[0];
        const orig = window[row.apply];
        const calls = stubApply(window.STATE_DECL[1].apply);
        window[row.apply] = undefined;
        const warned = captureWarn(function () { window.stateApply({ runState: 'idle' }); });
        window[row.apply] = orig;
        expect(warned.join(' ')).toContain(row.apply);
        expect(calls.length).toBe(1);
    });

    it('单件异常 → 隔离（其余仍执行）且出声', () => {
        const row = window.STATE_DECL[0];
        stubbed.push({ name: row.apply, orig: window[row.apply] });
        window[row.apply] = function () { throw new Error('boom'); };
        const calls = stubApply(window.STATE_DECL[1].apply);
        const warned = captureWarn(function () { window.stateApply({ runState: 'idle' }); });
        expect(warned.join(' ')).toContain('投影失败');
        expect(calls.length).toBe(1);
    });
});

describe('state.js 薄层（整段覆盖 + 按表分发）', () => {
    it('整段覆盖——缺省字段回落默认，字段面与契约一致', () => {
        window.stateApply({ runState: 'think' });
        expect(window.appState.runState).toBe('think');
        expect(Object.keys(window.appState).slice().sort()).toEqual(STATE_FIELDS.slice().sort());
        expect(window.appState.sessionId).toBe('');
        expect(window.appState.requests).toBe(0);
        expect(window.appState.note).toBeNull();
    });

    it('空段零动作（连接首帧前 state 未到——不清既有值）', () => {
        window.stateApply({ runState: 'tool' });
        window.stateApply(null);
        expect(window.appState.runState).toBe('tool');
    });

    it('接线哨兵——分发表在 chat.html 清单内', () => {
        expect(srcs).toContain('js/chat/state/registry.js');
    });
});

// ── 件 ↔ 表 一致性（A194 迁出后新增）──────────────
// 判据：owner='state' 的行，入口声明面在 `state/`（registry.js 除外）；owner='fx' 的在 `fx/`；
//       每个件文件至少有一个入口被表引用（孤儿件 = 表未接线 → 红）
describe('件 ↔ 表一致性（A194）', () => {
    it('owner 与实现目录一致，且无孤儿件', async () => {
        const fs = await import('node:fs');
        const path = await import('node:path');
        const url = await import('node:url');
        const chatDir = path.join(path.dirname(url.fileURLToPath(import.meta.url)), '..', '..', 'js', 'chat');
        const readDir = (name) => fs.readdirSync(path.join(chatDir, name)).filter((f) => f.endsWith('.js'));
        const readCode = (name, file) => fs.readFileSync(path.join(chatDir, name, file), 'utf-8');
        const stateFiles = readDir('state').filter((f) => f !== 'registry.js');
        const stateCode = stateFiles.map((f) => readCode('state', f)).join('\n');
        const fxCode = readDir('fx').map((f) => readCode('fx', f)).join('\n');
        const applies = window.STATE_DECL.map((r) => r.apply);
        for (const r of window.STATE_DECL) {
            const code = (r.owner === 'state') ? stateCode : fxCode;
            expect(code.indexOf('function ' + r.apply + '(') >= 0).toBe(true);
        }
        for (const f of stateFiles) {
            const code = readCode('state', f);
            const names = [];
            const re = /\bfunction\s+([A-Za-z_$][\w$]*)\s*\(/g;
            let m;
            while ((m = re.exec(code)) !== null) {
                names.push(m[1]);
            }
            expect(names.some((n) => applies.indexOf(n) >= 0)).toBe(true);
        }
    });
});
