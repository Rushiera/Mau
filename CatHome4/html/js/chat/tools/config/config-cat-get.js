// ═══════════════════════════════════════════
// chat/tools/config/config-cat-get.js —— config-cat-get / 读每猫配置（ConfigCat）
// 声明：折叠行读结构化头（target）+ 输入意图行；段结构归骨架 `text`（全量字段原文）。
// ═══════════════════════════════════════════

toolDecl('config-cat-get', {
    inputLines: function (a) {
        return ['读取每猫配置 ' + ovText(a.cat)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '每猫配置 ' + ovText(a.cat) + ovStat(r);
        }
        return '每猫配置 ' + ovMetaStr(h.meta, 'target') + ' · 全量字段';
    }
});
