// ═══════════════════════════════════════════
// chat/tools/search/web-fetch.js —— web-fetch / 网页资产下载（WebCat）
// 声明：图标 ⬇️ + 输入意图行；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('web-fetch', {
    icon: '⬇️',
    inputLines: function (a) {
        return ['登记下载 ' + ovText(a.url)];
    }
});
