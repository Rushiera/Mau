// ═══════════════════════════════════════════
// chat/tools/cs/cs-comment.js —— cs-comment / XML 注释增改（CsCat）
// 声明：折叠行读结构化头（target / type）+ 输入意图行（目标 → 类型）；输出段自然语言化（已写入 …）。
// ═══════════════════════════════════════════

toolDecl('cs-comment', {
    inputLines: function (a) {
        var target = ovText(a.class) + (a.member ? '.' + a.member : '');
        return ['写 ' + ovText(a.type) + ' 注释 → ' + target];
    },
    headline: function (a, r) {
        var target = ovText(a.class) + (a.member ? '.' + a.member : '');
        var h = ovHead(r);
        if (!h) {
            return '注释 ' + target + ovStat(r);
        }
        return '注释 ' + target + ' · ' + ovMetaStr(h.meta, 'type');
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var m = h.meta;
        return ['已写入 ' + ovMetaStr(m, 'target') + ' 的 ' + ovMetaStr(m, 'type') + ' 注释'];
    }
});
