// ═══════════════════════════════════════════
// chat/tools/search/web-search.js —— web-search / 联网搜索（SearchCat）
// 声明：专属图标 🌐 + 折叠行（引用条数 / 协议）+ 输入意图行；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('web-search', {
    icon: '🌐',
    inputLines: function (a) {
        return ['联网搜索 ' + ovText(a.query)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '联网搜索 ' + ovText(a.query) + ovStat(r);
        }
        var cites = ovMetaNum(h.meta, 'citations', 0);
        var cite = (cites > 0) ? (' · ' + cites + ' 条引用') : '';
        var proto = ovMetaStr(h.meta, 'protocol');
        var protoTail = (proto.length > 0) ? (' · ' + proto) : '';
        return '联网搜索 ' + ovText(a.query) + cite + protoTail;
    }
});
