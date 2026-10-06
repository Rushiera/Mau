// ═══════════════════════════════════════════
// chat/tools/text/text-append.js —— text-append / 追加（TextCat）
// 声明：折叠行自然语言 + 输入意图行（+ 号标记增量）。
// ═══════════════════════════════════════════

toolDecl('text-append', {
    inputLines: function (a) {
        return ['追加 ' + ovText(a.path) + ' · ' + ovSize(a.content)];
    },
    headline: function (a, r) {
        return '追加 ' + ovText(a.path) + ' · +' + ovSize(a.content);
    }
});
