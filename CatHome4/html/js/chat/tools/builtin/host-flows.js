// ═══════════════════════════════════════════
// chat/tools/builtin/host-flows.js —— host-flows / 运行 Flow 清单（内置）
// 声明：折叠行读结构化头（count）+ 输入意图行；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('host-flows', {
    inputLines: function () {
        return ['查看当前运行 Flow 清单'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return 'Flow 现状' + ovStat(r);
        }
        return 'Flow 现状 · ' + ovMetaNum(h.meta, 'items', 0) + ' 个';
    }
});
