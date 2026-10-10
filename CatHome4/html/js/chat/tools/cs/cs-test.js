// ═══════════════════════════════════════════
// chat/tools/cs/cs-test.js —— cs-test / 实机跑测（CsCat）
// 声明：折叠行读结构化头（ok / passed / failed / ms）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('cs-test', {
    inputLines: function (a) {
        var line = '跑测 ' + ovText(a.path);
        if (typeof a.filter === 'string' && a.filter.length > 0) {
            line = line + ' · filter=' + a.filter;
        }
        if (a.noBuild === 'true' || a.noBuild === true) {
            line = line + ' · 跳过编译';
        }
        return [line];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '跑测 ' + ovShort(a.path) + ovStat(r);
        }
        var m = h.meta;
        var ms = ovMetaNum(m, 'ms', -1);
        var tail = (ms >= 0) ? (' · ' + (ms / 1000).toFixed(1) + 's') : '';
        return '跑测 ' + ovShort(a.path) + ' · 通过 ' + ovMetaNum(m, 'passed', 0)
            + ' / 失败 ' + ovMetaNum(m, 'failed', 0) + tail;
    }
});
