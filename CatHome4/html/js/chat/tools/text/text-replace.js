// ═══════════════════════════════════════════
// chat/tools/text/text-replace.js —— text-replace / 锚点替换（TextCat）
// 声明：专属图标 🔄 + 折叠行自然语言（模式 / 处数）+ 输入意图行（旧 / 新锚点预览）。
// ═══════════════════════════════════════════

toolDecl('text-replace', {
    icon: '🔄',
    inputLines: function (a) {
        var head = '替换 ' + ovText(a.path) + (a.mode ? ' · 模式 ' + a.mode : '');
        return [head, '旧：' + ovPeek(a.old), '新：' + ovPeek(a.new)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '替换 ' + ovText(a.path) + ovMode(a) + ovReplaceTail(r);
        }
        return '替换 ' + ovMetaStr(h.meta, 'target') + ovMode(a) + ' · ' + ovMetaNum(h.meta, 'items', 0) + ' 处';
    }
});
