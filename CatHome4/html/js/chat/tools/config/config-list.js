// ═══════════════════════════════════════════
// chat/tools/config/config-list.js —— config-list / 配置项清单（ConfigCat）
// 声明：折叠行读结构化头（count / writable）+ 输入意图行；段结构归骨架 `listing`。
// ═══════════════════════════════════════════

toolDecl('config-list', {
    inputLines: function () {
        return ['列出全部配置项（schema 声明 + 落盘未声明）'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '配置列表' + ovStat(r);
        }
        var m = h.meta;
        return '配置列表 · ' + ovMetaNum(m, 'count', 0) + ' 项 · ' + ovMetaNum(m, 'writable', 0) + ' 可写';
    }
});
