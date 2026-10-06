// ═══════════════════════════════════════════
// chat/tools/text/text-read_lines.js —— text-read_lines / 行号区间读取（TextCat）
// 声明：折叠行自然语言 + 输入意图行；段结构归骨架 `lines`（行号列 + 内容）。
// ═══════════════════════════════════════════

toolDecl('text-read_lines', {
    inputLines: function (a) {
        return ['按行读取 ' + ovText(a.path) + ' · ' + ovRange(a)];
    },
    headline: function (a, r) {
        return '按行读取 ' + ovText(a.path) + ' · ' + ovRange(a) + ovStat(r);
    }
});
