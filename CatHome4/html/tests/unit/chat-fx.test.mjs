// ═══════════════════════════════════════════
// tests/unit/chat-fx.test.mjs —— 独立功能件（fx）回归网（A182）
//
// 覆盖（设计 §三 组 1 判据「各 fx 件」）：
//   ① 声明表 `FX_FEATURES` 五件齐备 + 字段面完整（id / name / inputs / outputs / init）
//   ② `fxBoot` —— 入口查空出声 + 一件抛错不拖累其余（异常隔离）
//   ③ controls —— 轮进行态判据与四按钮派生
//   ④ pending —— FIFO 出列 / 清队 / 空表隐藏 / HTML 转义
//   ⑤ cmdIntent —— 适用判据 / 解码接线 / 展开块挂载 / 未识别上报
//   ⑥ scroll / pet —— 入口就位与幂等（几何与素材面属运行态验收，jsdom 无布局不可测）
//
// 边界：jsdom 无布局引擎——**凡依赖几何 / 素材时序的行为不在本网**（运行态验收承担）；
//       本网守「声明面 ↔ 实现面」「启动链不静默失败」「纯逻辑分支」。
// ═══════════════════════════════════════════

import { beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { bootChatPage, clearAreas } from './chat-env.mjs';

beforeAll(async () => {
    await bootChatPage();
});

beforeEach(() => {
    clearAreas();
});

/** 捕获 console.warn 执行一段逻辑 */
function captureWarn(body) {
    const warned = [];
    const orig = console.warn;
    console.warn = function () { warned.push(Array.prototype.join.call(arguments, ' ')); };
    try {
        body();
    } finally {
        console.warn = orig;
    }
    return warned;
}

// ── ① / ② 声明表与启动 ───────────────────────
describe('声明表与启动（fx/registry）', () => {
    it('五件齐备且字段面完整', () => {
        const table = window.FX_FEATURES;
        expect(table.length).toBe(5);
        expect(table.map((f) => f.id).sort()).toEqual(['cmdIntent', 'controls', 'pending', 'pet', 'scroll']);
        for (const f of table) {
            expect(typeof f.name, f.id).toBe('string');
            expect(Array.isArray(f.inputs), f.id + ' inputs').toBe(true);
            expect(Array.isArray(f.outputs), f.id + ' outputs').toBe(true);
            expect(f.inputs.length, f.id + ' 输入源非空').toBeGreaterThan(0);
            expect(typeof f.id, 'id').toBe('string');
            expect(f.id.length, 'id 非空').toBeGreaterThan(0);
        }
    });

    it('init 名可解析或显式 null（不留悬空入口）', () => {
        for (const f of window.FX_FEATURES) {
            if (f.init === null) {
                continue;
            }
            expect(typeof window[f.init], f.id + ' 入口 ' + f.init).toBe('function');
        }
    });

    it('入口查空出声——表内填了不存在的入口名时告警且不中断', () => {
        const table = window.FX_FEATURES;
        const ghost = { id: 'ghost', name: '幽灵件', inputs: ['x'], outputs: ['y'], init: 'fxNoSuchInit' };
        table.push(ghost);
        let warned = [];
        try {
            warned = captureWarn(() => { window.fxBoot(); });
        } finally {
            table.pop();
        }
        expect(warned.some((w) => w.indexOf('独立功能未启动：ghost') >= 0)).toBe(true);
    });

    it('异常隔离——一件 init 抛错不拖累其余（探针件照常执行）', () => {
        const table = window.FX_FEATURES;
        let probeCalls = 0;
        window.fxProbeInit = function () { probeCalls = probeCalls + 1; };
        const bad = { id: 'bad', name: '坏件', inputs: ['x'], outputs: ['y'], init: 'fxBadInit' };
        const probe = { id: 'probe', name: '探针件', inputs: ['x'], outputs: ['y'], init: 'fxProbeInit' };
        window.fxBadInit = function () { throw new Error('boom'); };
        table.splice(1, 0, bad, probe);
        let warned = [];
        try {
            warned = captureWarn(() => { window.fxBoot(); });
        } finally {
            table.splice(1, 2);
        }
        expect(warned.some((w) => w.indexOf('独立功能启动失败：bad') >= 0)).toBe(true);
        expect(probeCalls).toBe(1);
    });
});

// ── ③ controls ───────────────────────────────
describe('按钮态（fx/controls）', () => {
    it('轮进行态判据——空 / idle 为否，其余为是', () => {
        expect(window.fxControlsRunning(null)).toBe(false);
        expect(window.fxControlsRunning({})).toBe(false);
        expect(window.fxControlsRunning({ runState: '' })).toBe(false);
        expect(window.fxControlsRunning({ runState: 'idle' })).toBe(false);
        expect(window.fxControlsRunning({ runState: 'run' })).toBe(true);
        expect(window.fxControlsRunning({ runState: 'tool' })).toBe(true);
    });

    it('元素缺失不抛（防御式）', () => {
        const node = document.getElementById('chatPause');
        const parent = node.parentNode;
        parent.removeChild(node);
        try {
            expect(() => { window.fxControlsApply({ runState: 'run' }); }).not.toThrow();
        } finally {
            parent.appendChild(node);
        }
    });
});

// ── ④ pending ────────────────────────────────
describe('插话队列（fx/pending）', () => {
    it('FIFO 出列——先入先出', () => {
        window.pendingAdd('一');
        window.pendingAdd('二');
        expect(document.querySelectorAll('#chatPendingPanel > div').length).toBe(2);
        window.pendingConsume();
        const html = document.getElementById('chatPendingPanel').innerHTML;
        expect(html).toContain('二');
        expect(html).not.toContain('一');
    });

    it('空队零动作 / 空表清空面板（:empty 隐藏）', () => {
        window.pendingConsume();
        window.pendingClear();
        expect(document.getElementById('chatPendingPanel').innerHTML).toBe('');
    });

    it('渲染转义——HTML 标签按文本落盘（不注入）', () => {
        window.pendingAdd('<img src=x onerror=alert(1)>');
        const panel = document.getElementById('chatPendingPanel');
        expect(panel.querySelector('img')).toBeNull();
        expect(panel.textContent).toContain('<img src=x');
    });
});

// ── ⑤ cmdIntent ──────────────────────────────
describe('命令解码显示（fx/cmd-intent）', () => {
    it('适用判据——仅 powershell / powershell7', () => {
        expect(window.fxCmdIntentApplies('powershell')).toBe(true);
        expect(window.fxCmdIntentApplies('powershell7')).toBe(true);
        expect(window.fxCmdIntentApplies('text-read')).toBe(false);
        expect(window.fxCmdIntentApplies('')).toBe(false);
    });

    it('解码——非适用工具返回 null（调用方回落骨架）', () => {
        expect(window.fxCmdIntentDecode('text-read', '{"path":"a"}')).toBeNull();
    });

    it('解码——适用工具返回 brief / detail / unknown', () => {
        const d = window.fxCmdIntentDecode('powershell', JSON.stringify({ command: 'git status' }));
        expect(d).toBeTruthy();
        expect(typeof d.brief).toBe('string');
        expect(d.brief.length).toBeGreaterThan(0);
        expect(typeof d.detail).toBe('string');
        expect(Array.isArray(d.unknown)).toBe(true);
    });

    it('展开块挂载——非空解码结果追加 .cmd-intent', () => {
        const host = window.el('div', 'detail');
        window.fxCmdIntentAttach(host, { detail: '读取文件 a.txt' });
        expect(host.querySelectorAll('.cmd-intent').length).toBe(1);
        expect(host.textContent).toContain('读取文件 a.txt');
    });

    it('展开块挂载——空解码结果零动作', () => {
        const host = window.el('div', 'detail');
        window.fxCmdIntentAttach(host, null);
        expect(host.childNodes.length).toBe(0);
    });

    it('未识别上报——经 cmdReportUnknown 转发（件内不含上报实现）', () => {
        const orig = window.cmdReportUnknown;
        const seen = [];
        window.cmdReportUnknown = function (u) { seen.push(u); };
        try {
            window.fxCmdIntentReport({ unknown: [{ seg: 'x' }] });
        } finally {
            window.cmdReportUnknown = orig;
        }
        expect(seen.length).toBe(1);
    });
});

// ── ⑥ scroll / pet ───────────────────────────
describe('滚动带与桌宠——入口就位（几何 / 素材面归运行态验收）', () => {
    it('滚动带——入口声明且重复启动幂等', () => {
        expect(typeof window.chatScrollInit).toBe('function');
        expect(() => { window.chatScrollInit(); window.chatScrollInit(); }).not.toThrow();
    });

    it('桌宠——入口声明 + 连接态置位不抛', () => {
        for (const n of ['chatPetInit', 'chatPetSync', 'chatPetSetOffline', 'chatPetSay']) {
            expect(typeof window[n], n).toBe('function');
        }
        expect(() => { window.chatPetSetOffline(true); window.chatPetSetOffline(false); }).not.toThrow();
    });
});
