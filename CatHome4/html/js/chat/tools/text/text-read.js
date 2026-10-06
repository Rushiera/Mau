// ═══════════════════════════════════════════
// chat/tools/text/text-read.js —— text-read / 全文读取（TextCat）
// 声明：折叠行自然语言 + 输入意图行；段结构归骨架 `file`（正文等宽）。
// ═══════════════════════════════════════════

toolDecl('text-read', {
    inputLines: function (a) {
        return ['读取 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        return '读取 ' + ovText(a.path) + ovStat(r);
    }
});
