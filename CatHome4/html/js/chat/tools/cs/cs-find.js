// ═══════════════════════════════════════════
// chat/tools/cs/cs-find.js —— cs-find / 符号查找（CsCat）
// 声明：折叠行读结构化头（items / projects）+ 输入意图行（名字 / 项目）；段结构归骨架 `matches`。
// ═══════════════════════════════════════════

toolDecl('cs-find', {
    inputLines: function (a) {
        return ['查找声明 ' + ovText(a.name), '项目 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var target = ovText(a.name);
        var h = ovHead(r);
        if (!h) {
            return '查找 ' + target + ovStat(r);
        }
        return '查找 ' + target + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 处 · ' + ovMetaNum(h.meta, 'projects', 0) + ' 项目';
    }
});
