// ═══════════════════════════════════════════
// chat/tools/vision/image-analyze.js —— image-analyze / 图像识别（VisionCat）
// 声明：输入图片预览（被识别的图直接看得见）+ 折叠行（字符数）+ 输入意图行；段结构归骨架 `text`。
// 路径形态交 `lib/images.js` 单一出口转取图端点——声明层不做路径知识。
// ═══════════════════════════════════════════

toolDecl('image-analyze', {
    inputImages: function (a) {
        if (!a || !a.path) {
            return [];
        }
        return [a.path];
    },
    inputLines: function (a) {
        return ['识别图片 ' + ovText(a.path),
            (a.question ? ('提示词 ' + ovPeek(a.question)) : '（默认描述）')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var name = ovShort(a.path);
        if (!h) {
            return '识别图片 ' + name + ovStat(r);
        }
        return '识别图片 ' + name + ' · ' + ovMetaNum(h.meta, 'chars', 0) + ' 字';
    }
});
