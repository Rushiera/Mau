// ═══════════════════════════════════════════
// chat/tools/builtin/Note.js —— Note / 任务追踪器（内置）
// 声明：专属图标 📌 + 折叠行（第 i/items 条 · 已完成 / 待完成）+ 输入意图行（写入计划 / 推进）。
// ═══════════════════════════════════════════

toolDecl('Note', {
    icon: '📌',
    inputLines: function (a) {
        if (a.action === 'set') {
            return ['写入计划 · ' + ovSize(a.content) + (a.force === true ? ' · 强制覆盖' : '')];
        }
        return ['推进到下一条任务'];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '任务追踪' + ovStat(r);
        }
        var m = h.meta;
        var state = ovMetaStr(m, 'state');
        if (state === 'done') {
            return '任务追踪 · 全部完成（' + ovMetaNum(m, 'items', 0) + ' 条）';
        }
        if (state === 'empty') {
            return '任务追踪 · 暂无计划';
        }
        return '任务追踪 · 第' + ovMetaNum(m, 'index', 0) + '/' + ovMetaNum(m, 'items', 0)
            + '条 · 已完成' + ovMetaNum(m, 'done', 0) + ' 待完成' + ovMetaNum(m, 'remain', 0);
    }
});
