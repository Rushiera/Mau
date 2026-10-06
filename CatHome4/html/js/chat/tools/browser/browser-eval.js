// ═══════════════════════════════════════════
// chat/tools/browser/browser-eval.js —— browser-eval / 页面内取值（BrowserCat）
// 声明：专属图标 ⌨️ + 折叠行（字符数）+ 输入意图行（表达式预览）。
// ═══════════════════════════════════════════

toolDecl('browser-eval', {
    icon: '⌨️',
    inputLines: function (a) {
        return ['执行 JS ' + ovPeek(a.expression)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '执行 JS' + ovStat(r);
        }
        return '执行 JS · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
