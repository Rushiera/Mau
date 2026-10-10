// ═══════════════════════════════════════════
// chat/tools/builtin/pack.js —— pack / 加载包注入（内置）
// 声明：专属图标 📚 + 折叠行（件数）+ 输入意图行。
// ═══════════════════════════════════════════

toolDecl('pack', {
    icon: '📚',
    inputLines: function (a) {
        return ['加载包 ' + ovText(a.key)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '加载包 ' + ovText(a.key) + ovStat(r);
        }
        return '加载包 ' + ovMetaStr(h.meta, 'target') + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 件';
    }
});
