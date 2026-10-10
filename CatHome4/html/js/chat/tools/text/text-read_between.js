// ═══════════════════════════════════════════
// chat/tools/text/text-read_between.js —— text-read_between / 锚点区间读取（TextCat）
// 声明：折叠行自然语言 + 输入意图行（锚点两端缺省显式化为「文件头 / 文件尾」）。
// ═══════════════════════════════════════════

toolDecl('text-read_between', {
    inputLines: function (a) {
        return ['区间读取 ' + ovText(a.path),
            '锚点 ' + ovText(a.str1 || '（文件头）') + ' ~ ' + ovText(a.str2 || '（文件尾）')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '区间读取 ' + ovText(a.path) + ovAnchor(a) + ovStat(r);
        }
        return '区间读取 ' + ovMetaStr(h.meta, 'target') + ovAnchor(a) + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 行';
    }
});
