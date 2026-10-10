// ═══════════════════════════════════════════
// chat/tools/browser/browser-headful.js —— browser-headful / 有头登录实例（BrowserCat · 域外专属）
// 声明：专属图标 🪟 + 折叠行（动作 + 字符数）+ 输入意图行（按 action 分支）。
// 域外专属——主干人工登录工具（open 起有头窗口登录；close 关有头、profile 落盘保留登录态）。
// ═══════════════════════════════════════════

toolDecl('browser-headful', {
    icon: '🪟',
    inputLines: function (a) {
        var act = a.action || 'open';
        if (act === 'close') {
            return ['关闭有头浏览器'];
        }
        return ['呼起有头浏览器' + (a.url ? (' · ' + ovText(a.url)) : '')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var act = h ? (ovMetaStr(h.meta, 'action') || 'open') : (a.action || 'open');
        if (!h) {
            return '有头 ' + act + ovStat(r);
        }
        return '有头 ' + act + ' · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
