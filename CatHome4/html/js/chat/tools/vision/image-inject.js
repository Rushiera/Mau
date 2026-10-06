// ═══════════════════════════════════════════
// chat/tools/vision/image-inject.js —— image-inject / 图片插入主干（VisionCat）
// 声明：专属图标 🖼️ + 输入图片预览（被插入的图直接看得见）+ 折叠行自然语言；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('image-inject', {
    icon: '🖼️',
    inputImages: function (a) {
        if (!a || !a.path) {
            return [];
        }
        return [a.path];
    },
    inputLines: function (a) {
        return ['插入主干 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        return '已插入主干 · ' + ovShort(a.path);
    }
});
