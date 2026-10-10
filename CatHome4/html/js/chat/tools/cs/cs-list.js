// ═══════════════════════════════════════════
// chat/tools/cs/cs-list.js —— cs-list / 类与成员签名清单（CsCat）
// 声明：折叠行读结构化头（target / items）+ 输入意图行；段结构归骨架 `listing`。
// ═══════════════════════════════════════════

toolDecl('cs-list', {
    inputLines: function (a) {
        if (a.class) {
            return ['列出 ' + ovText(a.path) + ' · 类 ' + a.class];
        }
        return ['列出 ' + ovText(a.path) + ' 的类'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '列出 ' + ovShort(a.path) + ovStat(r);
        }
        var proj = ovMetaStr(h.meta, 'target');
        return '列出 ' + (proj.length > 0 ? proj : ovShort(a.path)) + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 类';
    }
});
