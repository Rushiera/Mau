// ═══════════════════════════════════════════
// chat/tools/cs/cs-check.js —— cs-check / 语法层验证（CsCat）
// 声明：折叠行读结构化头（errors / warnings）+ 输入意图行；段结构归骨架 `diagnostics`（计数徽标 + 诊断逐行）。
// ═══════════════════════════════════════════

toolDecl('cs-check', {
    inputLines: function (a) {
        return ['语法检查 ' + ovText(a.path) + (a.full === true ? ' · 含警告' : '')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '语法检查 ' + ovShort(a.path) + ovStat(r);
        }
        return '语法检查 ' + ovShort(a.path) + ' · ' + ovMetaNum(h.meta, 'errors', 0) + ' 错 ' + ovMetaNum(h.meta, 'warnings', 0) + ' 警';
    }
});
