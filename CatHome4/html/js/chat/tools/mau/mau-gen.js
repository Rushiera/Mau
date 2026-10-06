// ═══════════════════════════════════════════
// chat/tools/mau/mau-gen.js —— mau-gen / 组翻译（不编译）（MauCat）
// 声明：折叠行读结构化头（ok / proj / steps / errors）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('mau-gen', {
    inputLines: function (a) {
        return ['组翻译（不编译）' + ovText(a.proj)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return 'Mau 生成 ' + ovShort(a.proj) + ovStat(r);
        }
        var m = h.meta;
        if (m.ok === false) {
            return 'Mau 生成 ' + ovMetaStr(m, 'proj') + ' · ' + ovMetaNum(m, 'errors', 0) + ' 个错误';
        }
        return 'Mau 生成 ' + ovMetaStr(m, 'proj') + ' · ' + ovMetaNum(m, 'steps', 0) + ' 步';
    }
});
