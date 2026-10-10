// ═══════════════════════════════════════════
// chat/tools/config/config-cat-set.js —— config-cat-set / 写每猫配置（ConfigCat）
// 声明：折叠行读结构化头（target）+ 输入意图行（猫 / 字段）。
// ═══════════════════════════════════════════

toolDecl('config-cat-set', {
    inputLines: function (a) {
        return ['设置每猫配置 ' + ovText(a.cat) + ' · ' + ovText(a.field)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '每猫配置 ' + ovText(a.cat) + ovStat(r);
        }
        return '每猫配置 ' + ovMetaStr(h.meta, 'target') + ' · ' + ovText(a.field) + ' 已更新';
    }
});
