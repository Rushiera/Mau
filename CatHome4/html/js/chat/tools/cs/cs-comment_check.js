// ═══════════════════════════════════════════
// chat/tools/cs/cs-comment_check.js —— cs-comment_check / 缺 summary 扫描（CsCat）
// 声明：折叠行读结构化头（missing / items）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('cs-comment_check', {
    inputLines: function (a) {
        return ['检查注释 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '注释检查 ' + ovShort(a.path) + ovStat(r);
        }
        var miss = ovMetaNum(h.meta, 'missing', 0);
        if (miss > 0) {
            return '注释检查 ' + ovShort(a.path) + ' · ' + miss + ' 处缺 summary';
        }
        return '注释检查 ' + ovShort(a.path) + ' · 齐全 ' + ovMetaNum(h.meta, 'items', 0) + ' 项';
    }
});
