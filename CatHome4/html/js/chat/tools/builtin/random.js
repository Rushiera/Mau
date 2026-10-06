// ═══════════════════════════════════════════
// chat/tools/builtin/random.js —— random / 随机整数（内置）
// 声明：专属图标 🎲 + 折叠行（区间 → 取值）+ 输入意图行（区间回显）。
// ═══════════════════════════════════════════

toolDecl('random', {
    icon: '🎲',
    inputLines: function (a) {
        return ['随机数 ' + ovText(a.min) + ' ~ ' + ovText(a.max)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '随机数' + ovStat(r);
        }
        var m = h.meta;
        return '随机数 [' + ovMetaNum(m, 'min', 0) + ',' + ovMetaNum(m, 'max', 0) + ') → ' + ovMetaNum(m, 'value', 0);
    }
});
