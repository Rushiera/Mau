// ═══════════════════════════════════════════
// chat/tools/file/file-tree.js —— file-tree / 递归目录树（FileCat）
// 声明：折叠行自然语言（条目数 + 截断总量）+ 输入意图行；段结构归骨架 `listing`。
// ═══════════════════════════════════════════

toolDecl('file-tree', {
    inputLines: function (a) {
        var extra = (a.limit === undefined) ? '' : ' · limit ' + a.limit;
        return ['展开 ' + ovText(a.path) + ' · depth ' + ovText(a.depth) + extra];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var cnt = h ? ovMetaNum(h.meta, 'items', 0) : ovItems(r);
        return '展开 ' + ovText(a.path) + ' · depth ' + ovText(a.depth) + ' · ' + cnt + ' 条目' + ovTotalTail(h ? h.body : r);
    }
});
