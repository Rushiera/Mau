// ═══════════════════════════════════════════
// chat/tools/cs/cs-find_ref.js —— cs-find_ref / 成员全引用（CsCat）
// 声明：折叠行读结构化头（items / projects）+ 输入意图行；段结构归骨架 `matches`（三列命中）。
// ═══════════════════════════════════════════

toolDecl('cs-find_ref', {
    inputLines: function (a) {
        return ['查找引用 ' + ovText(a.class) + '.' + ovText(a.member)];
    },
    headline: function (a, r) {
        var target = ovText(a.class) + '.' + ovText(a.member);
        var h = ovHead(r);
        if (!h) {
            return '引用 ' + target + ovStat(r);
        }
        return '引用 ' + target + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 处 · ' + ovMetaNum(h.meta, 'projects', 0) + ' 项目';
    }
});
