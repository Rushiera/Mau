// ═══════════════════════════════════════════
// chat/tools/temp/temp-info.js —— temp-info / 临时工具 Key 清单（TempToolCat）
// 声明：折叠行读结构化头（count）+ 输入意图行；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('temp-info', {
    inputLines: function () {
        return ['列出临时工具 Key（TempRegistry）'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '临时工具 Key' + ovStat(r);
        }
        var n = ovMetaNum(h.meta, 'items', 0);
        return '临时工具 · ' + (n > 0 ? (n + ' 个可用 Key') : '暂无注册');
    }
});
