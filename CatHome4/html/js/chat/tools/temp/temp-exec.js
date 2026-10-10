// ═══════════════════════════════════════════
// chat/tools/temp/temp-exec.js —— temp-exec / 万能执行（TempToolCat）
// 声明：折叠行读结构化头（key / chars）+ 输入意图行；段结构归骨架 `exec`。
// ═══════════════════════════════════════════

toolDecl('temp-exec', {
    inputLines: function (a) {
        return ['临时执行 ' + ovText(a.key) + ' · 入参 ' + ovSize(a.content)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '临时执行 ' + ovText(a.key) + ovStat(r);
        }
        return '临时执行 ' + ovMetaStr(h.meta, 'target') + ' · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
