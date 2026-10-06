// ═══════════════════════════════════════════
// chat/tools/file/file-copy.js —— file-copy / 复制（FileCat）
// 声明：专属图标 📋 + 折叠行自然语言（源 → 目标）+ 输入意图行（两行：源 / 目标）。
// ═══════════════════════════════════════════

toolDecl('file-copy', {
    icon: '📋',
    inputLines: function (a) {
        return ['复制 ' + ovText(a.src), '→ ' + ovText(a.dest)];
    },
    headline: function (a, r) {
        return '复制 ' + ovText(a.src) + ' → ' + ovText(a.dest);
    }
});
