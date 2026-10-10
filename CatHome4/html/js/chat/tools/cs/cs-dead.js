// ═══════════════════════════════════════════
// chat/tools/cs/cs-dead.js —— cs-dead / 零引用成员扫描（CsCat）
// 声明：折叠行读结构化头（dead）+ 输入意图行；段结构归骨架 `diagnostics`。
// ═══════════════════════════════════════════

toolDecl('cs-dead', {
    inputLines: function (a) {
        return ['扫描零引用 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '零引用扫描 ' + ovShort(a.path) + ovStat(r);
        }
        var dead = ovMetaNum(h.meta, 'dead', 0);
        return '零引用扫描 ' + ovShort(a.path) + ' · ' + (dead > 0 ? (dead + ' 处待清') : '干净');
    }
});
