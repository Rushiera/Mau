// ═══════════════════════════════════════════
// chat/tools/search/web-ds-search.js —— web-ds-search / DeepSeek 官方结构化检索（WebCat）
// 声明：专属图标 🔎 + 折叠行（结果条数 · 均分）+ 输入意图行（关键词与原始问题分列）；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('web-ds-search', {
    icon: '🔎',
    inputLines: function (a) {
        var lines = ['检索 ' + ovText(a.query)];
        if (a.ori_question) {
            lines.push('意图 ' + ovText(a.ori_question));
        }
        return lines;
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '检索 ' + ovText(a.query) + ovStat(r);
        }
        var cnt = ovMetaNum(h.meta, 'items', 0);
        var score = ovMetaNum(h.meta, 'avg_score', 0);
        var scoreTail = (score > 0) ? (' · 均分 ' + score.toFixed(1)) : '';
        return '检索 ' + ovText(a.query) + ' · ' + cnt + ' 条结果' + scoreTail;
    }
});
