// ═══════════════════════════════════════════
// chat/tools/browser/browser-read.js —— browser-read / 读取页面（BrowserCat）
// 声明：专属图标 📄 + 折叠行（模式 + 字符数）+ 输入意图行；段结构归骨架 `file`（正文）。
// ═══════════════════════════════════════════

toolDecl('browser-read', {
    icon: '📄',
    inputLines: function (a) {
        return ['读取页面 · ' + (a.mode || 'text')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '读取页面(' + (a.mode || 'text') + ')' + ovStat(r);
        }
        var mode = ovMetaStr(h.meta, 'mode');
        return '读取页面(' + (mode.length > 0 ? mode : 'text') + ') · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
