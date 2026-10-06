// ═══════════════════════════════════════════
// chat/tools/config/config-set.js —— config-set / 写配置项（ConfigCat）
// 声明：折叠行读结构化头（key）+ 输入意图行（key = value 预览）。
// ═══════════════════════════════════════════

toolDecl('config-set', {
    inputLines: function (a) {
        return ['设置配置 ' + ovText(a.key) + ' = ' + ovPeek(a.value)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '设置配置 ' + ovText(a.key) + ovStat(r);
        }
        return '设置配置 ' + ovMetaStr(h.meta, 'key') + ' · 已更新';
    }
});
