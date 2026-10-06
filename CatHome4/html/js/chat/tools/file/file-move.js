// ═══════════════════════════════════════════
// chat/tools/file/file-move.js —— file-move / 移动与重命名（FileCat）
// 声明：专属图标 📦 + 折叠行自然语言（源 → 目标）+ 输入意图行（两行：源 / 目标）。
// ═══════════════════════════════════════════

toolDecl('file-move', {
    icon: '📦',
    inputLines: function (a) {
        return ['移动 ' + ovText(a.src), '→ ' + ovText(a.dest)];
    },
    headline: function (a, r) {
        return '移动 ' + ovText(a.src) + ' → ' + ovText(a.dest);
    }
});
