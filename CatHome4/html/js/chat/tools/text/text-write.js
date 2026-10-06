// ═══════════════════════════════════════════
// chat/tools/text/text-write.js —— text-write / 覆写（TextCat）
// 声明：折叠行自然语言 + 输入意图行（规模 = 写入内容字符数）。
// ═══════════════════════════════════════════

toolDecl('text-write', {
    inputLines: function (a) {
        return ['写入 ' + ovText(a.path) + ' · ' + ovSize(a.content)];
    },
    headline: function (a, r) {
        return '写入 ' + ovText(a.path) + ' · ' + ovSize(a.content);
    }
});
