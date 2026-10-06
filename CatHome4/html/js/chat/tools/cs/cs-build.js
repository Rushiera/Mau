// ═══════════════════════════════════════════
// chat/tools/cs/cs-build.js —— cs-build / 实机编译（CsCat）
// 声明：折叠行读结构化头（ok / errors / warnings / ms）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('cs-build', {
    inputLines: function (a) {
        return ['编译 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '编译 ' + ovShort(a.path) + ovStat(r);
        }
        var ms = ovMetaNum(h.meta, 'ms', -1);
        var tail = (ms >= 0) ? (' · ' + (ms / 1000).toFixed(1) + 's') : '';
        return '编译 ' + ovShort(a.path) + ' · ' + (h.meta.ok === true ? '成功' : '失败')
            + ' ' + ovMetaNum(h.meta, 'errors', 0) + ' 错 ' + ovMetaNum(h.meta, 'warnings', 0) + ' 警' + tail;
    }
});
