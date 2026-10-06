// ═══════════════════════════════════════════
// chat/tools/browser/browser-shot.js —— browser-shot / 截图（BrowserCat）
// 声明：专属图标 📷 + 折叠行（整页 / 视口）+ 输入意图行。
// ═══════════════════════════════════════════

toolDecl('browser-shot', {
    icon: '📷',
    inputLines: function (a) {
        return ['截图页面' + (a.full ? '（整页）' : '（视口）')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '截图页面' + ovStat(r);
        }
        return '截图页面' + (h.meta.full === true ? '（整页）' : '（视口）');
    }
});
