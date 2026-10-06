// ═══════════════════════════════════════════
// chat/tools/browser/browser-open.js —— browser-open / 打开网页（BrowserCat）
// 声明：专属图标 🌐 + 折叠行自然语言 + 输入意图行；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('browser-open', {
    icon: '🌐',
    inputLines: function (a) {
        return ['打开网页 ' + ovText(a.url)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '打开网页 ' + ovShort(a.url) + ovStat(r);
        }
        return '打开网页 ' + ovShort(a.url);
    }
});
