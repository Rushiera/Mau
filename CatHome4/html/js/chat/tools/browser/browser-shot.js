// ═══════════════════════════════════════════
// chat/tools/browser/browser-shot.js —— browser-shot / 截图（BrowserCat）
// 声明：专属图标 📷 + 折叠行（整页 / 视口）+ 输入意图行。
// ═══════════════════════════════════════════

toolDecl('browser-shot', {
    icon: '📷',
    inputLines: function (a) {
        return ['截图页面' + (a.full ? '（整页）' : '（视口）')];
    },
    outputImages: function (r) {
        // 截图缩略图——结果正文形如 `file=<绝对路径> | full=… | px=… | bytes=…`；
        // 取 file= 后至分隔符前的路径，交 images 单一出口渲染（路径须在受控根内才可取图）
        if (typeof r !== 'string' || r.length === 0) { return []; }
        var m = r.match(/file=([^|\r\n]+)/);
        if (!m) { return []; }
        var p = m[1].replace(/^\s+|\s+$/g, '');
        return p.length > 0 ? [p] : [];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '截图页面' + ovStat(r);
        }
        return '截图页面' + (h.meta.full === true ? '（整页）' : '（视口）');
    }
});
