// ═══════════════════════════════════════════
// tests/unit/chat-reason.test.mjs —— reason 块载荷渲染（A199 逐 type 核对）
//
// 覆盖（契约 §12.5 的 reason 字段面）：
//   ① 头行统计四件——字符数 / 行数 / 用时 / 速率（后端载荷直用；行数为显示派生）
//   ② 缺失路径——durMs 未记录 → 时间与速率位跳过 + 尾附「未统计」
//   ③ 压缩档折叠提示——`…已折叠 N行 M字符…`（N/M = 首末行之间省略段规模）
//   ④ 展开档全文 + 切档交互收窄到头行（正文点击不切档）
//
// 边界：只测 reason 一件的载荷语义（A198「逐 type 核对」的 reason 项）——
//       声明面 / 形态契约归 chat-blocks.test.mjs。
// ═══════════════════════════════════════════

import { beforeAll, describe, expect, it } from 'vitest';
import { bootChatPage } from './chat-env.mjs';

beforeAll(async () => {
    await bootChatPage();
});

/// 统计项文本——头行 `.ct-stat`（唯一统计出口）
function statOf(node) {
    return node.querySelector('.ct-stat').textContent;
}

/// 点按模拟——无 mousedown 记录时 press 判据直接触发 toggle
/// （测试基座只暴露 Event——未暴露 MouseEvent；click 只需可冒泡）
function clickOn(el) {
    el.dispatchEvent(new Event('click', { bubbles: true }));
}

/// 造块——chars 按正文长度补齐（与后端口径一致：chars = text.Length）
function makeBlock(text, durMs, cps) {
    return window.buildReasonBlock({ text: text, chars: text.length, durMs: durMs, cps: cps });
}

describe('reason 块——头行统计（A199）', () => {
    it('四件齐备——字符数 / 行数 / 用时 / 速率', () => {
        const text = '一\n二\n三';
        const stat = statOf(makeBlock(text, 3200, 133.8));
        expect(stat).toContain(text.length + ' 字符');
        expect(stat).toContain('3 行');
        expect(stat).toContain('3.2s');
        expect(stat).toContain('133.8 字符/s');
    });

    it('缺失路径——durMs 未记录：时间 / 速率位跳过 + 尾附「未统计」', () => {
        const text = '一\n二';
        const stat = statOf(makeBlock(text, -1, -1));
        expect(stat).toContain('未统计');
        expect(stat).not.toContain('字符/s');
        expect(stat).toContain(text.length + ' 字符');
        expect(stat).toContain('2 行');
    });

    it('标签在统计之先——Think + 统计同处头行', () => {
        const head = makeBlock('一\n二', 1000, 5).querySelector('.ct-head');
        expect(head.querySelector('.ct-label').textContent).toBe('Think');
        expect(head.children.length).toBe(2);
    });
});

describe('reason 块——两档正文', () => {
    it('压缩档——≥3 行：首行 + 折叠提示 + 末行', () => {
        const text = '第一行\n第二行\n第三行\n第四行';
        const peek = makeBlock(text, 1000, 5).querySelector('.ct-peek').textContent;
        const hiddenChars = text.length - '第一行'.length - '第四行'.length - 2;
        expect(peek).toContain('…已折叠 2行 ' + hiddenChars + '字符…');
        expect(peek.indexOf('第一行')).toBe(0);
        expect(peek.endsWith('第四行')).toBe(true);
    });

    it('压缩档——<3 行不折叠（原文直出）', () => {
        const text = '一\n二';
        expect(makeBlock(text, 1000, 5).querySelector('.ct-peek').textContent).toBe(text);
    });

    it('展开档——.ct-full 承载全文（两档同置 DOM）', () => {
        const text = 'A\nB\nC';
        expect(makeBlock(text, 1000, 5).querySelector('.ct-full').textContent).toBe(text);
    });
});

describe('reason 块——切档交互', () => {
    it('热区仅头行——正文点击不切档，头行点击切 .full', () => {
        const node = makeBlock('A\nB\nC', 1000, 5);
        const box = node.querySelector('.chat-think');
        clickOn(node.querySelector('.ct-body'));
        expect(box.classList.contains('full')).toBe(false);
        clickOn(node.querySelector('.ct-head'));
        expect(box.classList.contains('full')).toBe(true);
        clickOn(node.querySelector('.ct-head'));
        expect(box.classList.contains('full')).toBe(false);
    });
});
