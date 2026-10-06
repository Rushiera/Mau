// ═══════════════════════════════════════════
// chat/tools/file/file-delete.js —— file-delete / 软删除入回收站（FileCat）
// 声明：专属图标 🗑️ + 折叠行自然语言 + 输入意图行（回收站语义显式）。
// ═══════════════════════════════════════════

toolDecl('file-delete', {
    icon: '🗑️',
    inputLines: function (a) {
        return ['删除 ' + ovText(a.path) + ' → 回收站'];
    },
    headline: function (a, r) {
        return '删除 ' + ovText(a.path) + ' → 回收站';
    }
});
