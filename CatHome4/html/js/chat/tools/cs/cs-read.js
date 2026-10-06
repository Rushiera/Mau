// ═══════════════════════════════════════════
// chat/tools/cs/cs-read.js —— cs-read / 成员源码读取（CsCat）
// 声明：折叠行读结构化头（start / end 行区间）+ 输入意图行（类.成员 / 项目）；
//       段结构归骨架 `lines`（行号列 + 内容，start 驱动行号基准）。
// ═══════════════════════════════════════════

toolDecl('cs-read', {
    inputLines: function (a) {
        var target = ovText(a.class) + (a.member ? '.' + a.member : '');
        return ['读取 ' + target, '项目 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var target = ovText(a.class) + (a.member ? '.' + a.member : '');
        var h = ovHead(r);
        if (!h) {
            return '读取 ' + target + ovStat(r);
        }
        var start = ovMetaNum(h.meta, 'start', -1);
        var range = (start > 0) ? (' · L' + start + '-' + ovMetaNum(h.meta, 'end', start)) : '';
        return '读取 ' + target + range;
    }
});
