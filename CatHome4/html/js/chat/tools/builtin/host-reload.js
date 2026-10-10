// ═══════════════════════════════════════════
// chat/tools/builtin/host-reload.js —— host-reload / 热重载语料 dll（内置）
// 声明：折叠行读结构化头（target / oldId / newId）+ 输入意图行；段结构归骨架 `exec`。
// ═══════════════════════════════════════════

toolDecl('host-reload', {
    inputLines: function (a) {
        return ['热重载组 ' + ovText(a.cat)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '热重载 ' + ovText(a.cat) + ovStat(r);
        }
        var m = h.meta;
        return '热重载 ' + ovMetaStr(m, 'target') + ' · #' + ovMetaNum(m, 'oldId', 0) + ' → #' + ovMetaNum(m, 'newId', 0);
    }
});
