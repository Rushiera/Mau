// ═══════════════════════════════════════════
// chat/tools/builtin/time.js —— time / 当前日期时间（内置）
// 声明：专属图标 🕒 + 折叠行（时间戳）+ 输入意图行。
// ═══════════════════════════════════════════

toolDecl('time', {
    icon: '🕒',
    inputLines: function () {
        return ['获取当前时间'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '时间' + ovStat(r);
        }
        return '时间 · ' + ovMetaStr(h.meta, 'ts');
    }
});
