// ═══════════════════════════════════════════
// chat/tools/majordomo/restart-host.js —— restart-host / 宿主原地重启（特权）
// 声明：折叠行读结构化头（target / push）+ 输入意图行；段结构归骨架 `exec`。
// ═══════════════════════════════════════════

toolDecl('restart-host', {
    inputLines: function () {
        return ['请求宿主原地重启（不编译 / 不覆盖）'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '宿主原地重启' + ovStat(r);
        }
        var target = ovMetaStr(h.meta, 'target');
        return '宿主原地重启 · 目标 ' + (target.length > 0 ? target : '默认') + (h.meta.push ? ' · 带回执' : '');
    }
});
