// ═══════════════════════════════════════════
// chat/tools/mau/mau-verify.js —— mau-verify / 语料全链检查（MauCat）
// 声明：折叠行读结构化头（ok / errors / reports）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('mau-verify', {
    inputLines: function (a) {
        return ['全链检查 ' + ovText(a.file)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return 'Mau 验证 ' + ovShort(a.file) + ovStat(r);
        }
        var m = h.meta;
        if (m.ok === false) {
            return 'Mau 验证 ' + ovShort(a.file) + ' · ' + ovMetaNum(m, 'errors', 0) + ' 个错误';
        }
        return 'Mau 验证 ' + ovShort(a.file) + ' · 通过（' + ovMetaNum(m, 'reports', 0) + ' 报告）';
    }
});
