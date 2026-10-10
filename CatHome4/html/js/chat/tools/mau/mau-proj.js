// ═══════════════════════════════════════════
// chat/tools/mau/mau-proj.js —— mau-proj / 组翻译 + 编译（MauCat）
// 声明：折叠行读结构化头（ok / target / items / build / errors）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('mau-proj', {
    inputLines: function (a) {
        return ['组翻译 + 编译 ' + ovText(a.proj) + (a.build === true ? ' · build' : '')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '组翻译 ' + ovShort(a.proj) + ovStat(r);
        }
        var m = h.meta;
        if (m.ok === false) {
            return '组翻译 ' + ovMetaStr(m, 'target') + ' · ' + ovMetaNum(m, 'errors', 0) + ' 个错误';
        }
        return '组翻译 ' + ovMetaStr(m, 'target') + ' · ' + ovMetaNum(m, 'items', 0) + ' 步' + (m.build === true ? ' · 已编译' : '');
    }
});
