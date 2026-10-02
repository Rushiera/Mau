// ═══════════════════════════════════════════
// tests/unit/frontend-symbols.test.mjs —— 前端符号自检（零依赖静态检查）
//
// 定位：抓「引用了**任何地方都没声明过**的全局名」——A167 期 `toolIcon` 漏网（lib 实际名
//       `iconOf`）即此类；`node --check` 只查语法、不解析作用域，看不见这种错。
//
// 判据：`js/chat/**` 内所有「裸调用 NAME(」的名字，必须
//   ① 在本目录内**声明过**——函数声明（含缩进）· `var NAME =`（含缩进）· 函数形参，或
//   ② 属内置白名单，或
//   ③ 出现在 `typeof NAME === 'function'` 守卫里（防御式可选钩子——声明了「可能不存在」）。
//   差集非空即失败，附「名字 @ 文件:行」。
//
// 口径取舍：**宁漏勿误报**——不做作用域分析，任何同名的局部/形参都视为已声明。
//       因此它抓的是「全仓零声明的裸调用」（`toolIcon` 正是此类），不会抓拼错属性、未使用变量。
// ═══════════════════════════════════════════

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it, expect } from 'vitest';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'js', 'chat');

// 语言关键字 + 宿主内置——裸调用的合法来源
const BUILTIN = new Set([
    // 语句关键字（`catch (` / `if (` 等会被裸调用正则命中）
    'if', 'for', 'while', 'switch', 'catch', 'function', 'return', 'typeof', 'new', 'delete',
    'void', 'in', 'of', 'do', 'else', 'try', 'case', 'break', 'continue', 'throw', 'yield',
    'await', 'class', 'import', 'export', 'default',
    // 全局函数
    'parseInt', 'parseFloat', 'isNaN', 'isFinite', 'encodeURIComponent', 'decodeURIComponent',
    'encodeURI', 'decodeURI', 'setTimeout', 'setInterval', 'clearTimeout', 'clearInterval',
    'requestAnimationFrame', 'cancelAnimationFrame', 'fetch', 'alert', 'confirm', 'prompt',
    'queueMicrotask', 'structuredClone',
    // 内建构造器与命名空间
    'String', 'Number', 'Boolean', 'Array', 'Object', 'JSON', 'Math', 'Date', 'RegExp',
    'Error', 'TypeError', 'RangeError', 'Map', 'Set', 'WeakMap', 'WeakSet', 'Promise',
    'Symbol', 'BigInt', 'Intl', 'Proxy', 'Reflect',
    // 浏览器宿主构造器
    'EventSource', 'WebSocket', 'XMLHttpRequest', 'Blob', 'FileReader', 'FormData', 'URL',
    'URLSearchParams', 'TextEncoder', 'TextDecoder', 'AbortController', 'Image', 'Audio'
]);

// 递归收集 .js 文件
function jsFiles(dir) {
    const out = [];
    for (const name of readdirSync(dir)) {
        const p = join(dir, name);
        if (statSync(p).isDirectory()) {
            out.push(...jsFiles(p));
            continue;
        }
        if (name.endsWith('.js')) {
            out.push(p);
        }
    }
    return out;
}

describe('前端符号自检', () => {
    it('js/chat/** 无零声明的裸调用', () => {
        const files = jsFiles(ROOT);
        const declared = new Set();
        const calls = [];

        for (const file of files) {
            const rel = file.slice(ROOT.length + 1);
            const lines = readFileSync(file, 'utf-8').split(/\r?\n/);
            for (let i = 0; i < lines.length; i = i + 1) {
                const raw = lines[i];
                const trimmed = raw.replace(/^\s+/, '');
                // 行首注释跳过（注释里提到的历史名不算引用）
                if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) {
                    continue;
                }
                // 字符串与正则字面量剥离——引号 / 斜杠内是内容、不参与求值
                // （CSS `var(--x)` · 文案 `'Note ('` · 规则表 `/^git(\.exe)?/` 均非调用）
                const text = raw
                    .replace(/'(?:[^'\\]|\\.)*'/g, "''")
                    .replace(/"(?:[^"\\]|\\.)*"/g, '""')
                    .replace(/`(?:[^`\\]|\\.)*`/g, '``')
                    .replace(/\/(?:[^\n\/\\\[]|\\.|\[(?:[^\]\\]|\\.)*\])+\/[gimsuy]*/g, '/re/');
                let m;
                // 声明 ①：函数（含缩进——局部函数也算「声明过」，宁宽勿误报）
                const reDefFn = /\bfunction\s+([A-Za-z_$][\w$]*)\s*\(/g;
                while ((m = reDefFn.exec(text)) !== null) {
                    declared.add(m[1]);
                }
                // 声明 ②：变量
                const reDefVar = /\bvar\s+([A-Za-z_$][\w$]*)\s*=/g;
                while ((m = reDefVar.exec(text)) !== null) {
                    declared.add(m[1]);
                }
                // 声明 ③：函数形参（含匿名函数）
                const reParam = /\bfunction\s*[A-Za-z_$]*\s*\(([^)]*)\)/g;
                while ((m = reParam.exec(text)) !== null) {
                    const parts = m[1].split(',');
                    for (let k = 0; k < parts.length; k = k + 1) {
                        const nm = parts[k].replace(/=.*$/, '').replace(/\s+/g, '');
                        if (/^[A-Za-z_$][\w$]*$/.test(nm)) {
                            declared.add(nm);
                        }
                    }
                }
                // 声明 ④：防御式可选钩子——`typeof NAME === 'function'` 即声明「可能不存在」
                // 🔴 在 raw 上扫——`'function'` 是语法形态，字符串剥离后会失配
                const reTypeof = /\btypeof\s+([A-Za-z_$][\w$]*)\s*===?\s*['"]function['"]/g;
                while ((m = reTypeof.exec(raw)) !== null) {
                    declared.add(m[1]);
                }
                // 引用：裸调用（前置字符非「.」非标识符字符——排除 obj.method(）
                const reCall = /(^|[^.\w$])([A-Za-z_$][\w$]*)\s*\(/g;
                while ((m = reCall.exec(text)) !== null) {
                    calls.push({ name: m[2], where: rel + ':' + (i + 1) });
                }
            }
        }

        const missing = calls.filter(function (c) {
            return !declared.has(c.name) && !BUILTIN.has(c.name);
        });
        const report = missing.map(function (c) {
            return c.name + ' @ ' + c.where;
        });
        expect(report).toEqual([]);
    });
});
