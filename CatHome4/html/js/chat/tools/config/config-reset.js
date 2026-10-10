// ═══════════════════════════════════════════
// chat/tools/config/config-reset.js —— config-reset / 还原默认值（ConfigCat）
// 声明：折叠行读结构化头（scope / target / items）+ 输入意图行（单键 / 全部两态）。
// ═══════════════════════════════════════════

toolDecl('config-reset', {
    inputLines: function (a) {
        if (a.key) {
            return ['还原配置 ' + a.key + ' 为默认'];
        }
        return ['还原全部可写配置为默认'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '还原配置' + ovStat(r);
        }
        var m = h.meta;
        if (ovMetaStr(m, 'scope') === 'all') {
            return '还原全部可写配置 · ' + ovMetaNum(m, 'items', 0) + ' 项';
        }
        return '还原配置 ' + ovMetaStr(m, 'target') + ' · 默认值';
    }
});
