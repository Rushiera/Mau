// ═══════════════════════════════════════════
// tests/unit/chat-blocks.test.mjs —— 契约块型结构面回归网（A182）
//
// 覆盖（契约 §12.4 / §12.5 的**结构面**）：
//   ① 12 类块型（persist 9 + live 3）空载荷渲染不抛
//   ② 行的形态 / 行语义 / data-type 全部由 `BLOCK_DECL` 表驱动（渲染件不自拼类名）
//   ③ 形态契约——气泡仅 `user` / `text` / `gap_text`（2026-10-03 莎裁 + A188 分型：间隙文本
//      自 `text` 独立为条目型，外观形态不变——仍是会被人当对话内容读的模型输出）
//   ④ 取用口回落语义（未声明 type → 朴素件 + assistant 行 + reply 刻度）
//
// 边界：**不测块内载荷语义**（各 type 的字段渲染是否好看 / 完整）——那是 A198「逐 type 核对」的范围；
//       本网守的是「声明面 ↔ 实现面不漂移」与「结构契约不破」。
// ═══════════════════════════════════════════

import { beforeAll, describe, expect, it } from 'vitest';
import { bootChatPage } from './chat-env.mjs';

beforeAll(async () => {
    await bootChatPage();
});

const PERSIST_TYPES = ['user', 'text', 'gap_text', 'reason', 'toolcard', 'retry', 'error', 'inject_report', 'roundsum'];
const LIVE_TYPES = ['stream.text', 'stream.reason', 'toolcard.pending'];

/// 块体类例外——思考族自持 `.chat-think`、工具族自持 `.chat-tool`（`BLOCK_DECL` 未建模这两族块体；
/// 形态判据不受影响——`form:'plain'` 语义 =「不作对话内容读」，与二者一致，故非渲染缺陷）。
/// 🔴 这是**显式登记**而非放宽断言：新增例外必须来此登记，否则测试变红。
/// 口径统一（块体族是否入表 / `body` 列是否改全类名）归 A198「逐 type 核对」。
const BODY_EXCEPTIONS = {
    'reason': 'chat-think',
    'stream.reason': 'chat-think',
    'toolcard': 'chat-tool',
    'toolcard.pending': 'chat-tool'
};

/** 断言——行元素符合 `BLOCK_DECL` 声明（行类 / data-type / 块体类） */
function expectRowContract(node, type) {
    const decl = window.BLOCK_DECL[type];
    expect(decl, type).toBeTruthy();
    expect(node.classList.contains('chat-row'), type + ' 行基类').toBe(true);
    const rowTokens = decl.row.split(' ');
    for (const tok of rowTokens) {
        expect(node.classList.contains(tok), type + ' 行语义 ' + tok).toBe(true);
    }
    expect(node.getAttribute('data-type'), type + ' data-type').toBe(type);
    const exception = BODY_EXCEPTIONS[type];
    if (exception) {
        expect(node.querySelectorAll('.' + exception).length, type + ' 块体类 ' + exception).toBeGreaterThan(0);
        return;
    }
    const form = window.formClass(type);
    const holder = node.querySelectorAll('.' + form)[0];
    expect(holder, type + ' 形态类 ' + form).toBeTruthy();
    const bodyTokens = window.bodyClass(type).split(' ');
    for (const tok of bodyTokens) {
        expect(holder.classList.contains(tok), type + ' 块体语义 ' + tok).toBe(true);
    }
}

describe('块型渲染——结构契约', () => {
    it('persist 八类——空载荷不抛 + 行契约齐备', () => {
        for (const t of PERSIST_TYPES) {
            const fn = window.PERSIST_RENDERERS[t];
            expect(typeof fn, t + ' 渲染函数').toBe('function');
            const node = fn({});
            expect(node, t + ' 产出').toBeTruthy();
            expectRowContract(node, t);
        }
    });

    it('live 三类——空载荷不抛 + 行契约齐备', () => {
        for (const t of LIVE_TYPES) {
            const fn = window.LIVE_RENDERERS[t];
            expect(typeof fn, t + ' 渲染函数').toBe('function');
            const node = fn({});
            expect(node, t + ' 产出').toBeTruthy();
            expectRowContract(node, t);
        }
    });

    it('登记的渲染函数数 == 声明表行数（新增 type 必须声明面与实现面齐备）', () => {
        const declared = Object.keys(window.BLOCK_DECL).length;
        const implemented = Object.keys(window.PERSIST_RENDERERS).length + Object.keys(window.LIVE_RENDERERS).length;
        expect(implemented).toBe(declared);
    });
});

describe('形态契约', () => {
    it('气泡形态仅 user / text / gap_text（其余 9 类为朴素件）', () => {
        const bubbles = Object.keys(window.BLOCK_DECL).filter((t) => window.BLOCK_DECL[t].form === 'bubble');
        expect(bubbles.sort()).toEqual(['gap_text', 'text', 'user']);
    });

    it('取用口——形态 / 块体 / 行 / 刻度四口语义', () => {
        expect(window.formClass('user')).toBe('chat-bubble');
        expect(window.formClass('toolcard')).toBe('chat-plain');
        expect(window.bodyClass('error')).toBe('chat-plain error');
        expect(window.bodyClass('user')).toBe('chat-bubble');
        expect(window.tickRole('user')).toBe('user');
        expect(window.tickRole('roundsum')).toBe('sum');
    });

    it('未声明 type 回落——朴素件 + assistant 行 + reply 刻度（不出气泡）', () => {
        expect(window.formClass('nope')).toBe('chat-plain');
        expect(window.bodyClass('nope')).toBe('chat-plain');
        expect(window.tickRole('nope')).toBe('reply');
        expect(window.tickRole('')).toBe('reply');
        const row = window.blockRow('nope');
        expect(row.classList.contains('assistant')).toBe(true);
        expect(row.getAttribute('data-type')).toBe('nope');
    });
});
