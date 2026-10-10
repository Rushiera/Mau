// ═══════════════════════════════════════════
// chat/tools/text/text-grep.js —— text-grep / 内容关键词搜索（TextCat）
// 声明：折叠行自然语言（命中数）+ 输入意图行；段结构归骨架 `matches`（路径 / 行号 / 上下文三列）。
// ═══════════════════════════════════════════

toolDecl('text-grep', {
    inputLines: function (a) {
        var pat = a.pattern ? ' · 文件名 ' + a.pattern : '';
        return ['检索 ' + ovText(a.dir) + ' · 含 ' + ovText(a.keyword) + pat];
    },
    headline: function (a, r) {
        var pat = a.pattern ? ' · 文件名 ' + a.pattern : '';
        var h = ovHead(r);
        if (!h) {
            return '检索 ' + ovText(a.dir) + ' · 含 ' + ovText(a.keyword) + pat + ' · ' + ovItems(r) + ' 命中';
        }
        return '检索 ' + ovMetaStr(h.meta, 'target') + ' · 含 ' + ovText(a.keyword) + pat + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 命中';
    }
});
