// ═══════════════════════════════════════════
// chat/tools/file/file-find.js —— file-find / 文件名 glob 搜索（FileCat）
// 声明：专属图标 🔍 + 折叠行自然语言（条目数 + 截断总量）+ 输入意图行。
// ═══════════════════════════════════════════

toolDecl('file-find', {
    icon: '🔍',
    inputLines: function (a) {
        return ['搜索 ' + ovText(a.dir) + ' · glob ' + ovText(a.pattern)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var cnt = h ? ovMetaNum(h.meta, 'items', 0) : ovItems(r);
        return '搜索 ' + ovText(a.dir) + ' · glob ' + ovText(a.pattern) + ' · ' + cnt + ' 条目' + ovTotalTail(h ? h.body : r);
    }
});
