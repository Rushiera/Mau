// ═══════════════════════════════════════════
// chat/tools/search/web-fetch-jobs.js —— web-fetch-jobs / 下载任务管理（WebCat）
// 声明：图标 ⬇️ + 输入意图行；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('web-fetch-jobs', {
    icon: '⬇️',
    inputLines: function (a) {
        var action = ovText(a.action);
        return ['下载任务 ' + ((action.length > 0) ? action : 'list')];
    }
});
