// ═══════════════════════════════════════════
// chat/tools/browser/browser-tabs.js —— browser-tabs / 页签管理（BrowserCat）
// 声明：专属图标 🗂️ + 折叠行（动作 + 字符数）+ 输入意图行（按 action 分支）。
// ═══════════════════════════════════════════

toolDecl('browser-tabs', {
    icon: '🗂️',
    inputLines: function (a) {
        var act = a.action || 'list';
        if (act === 'new') {
            return ['新开页签 ' + ovText(a.url || '（空白页）')];
        }
        if (act === 'select') {
            return ['切换页签 ' + ovShort(a.target)];
        }
        if (act === 'close') {
            return ['关闭页签 ' + ovShort(a.target)];
        }
        return ['列出页签'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var act = h ? (ovMetaStr(h.meta, 'action') || 'list') : (a.action || 'list');
        if (!h) {
            return '页签 ' + act + ovStat(r);
        }
        return '页签 ' + act + ' · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
