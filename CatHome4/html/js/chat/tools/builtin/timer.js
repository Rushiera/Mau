// ═══════════════════════════════════════════
// chat/tools/builtin/timer.js —— timer / 登记延迟指令注入（内置）
// 声明：专属图标 ⏰ + 折叠行（时长 + 循环 + 到点时刻）+ 输入意图行（含注入内容预览）；排程语义（闹钟必响）。
// ═══════════════════════════════════════════

toolDecl('timer', {
    icon: '⏰',
    inputLines: function (a) {
        var d = ovDurText(a);
        var loop = (a.loop === true) ? ' · 循环' : '';
        return ['登记定时注入' + ((d.length > 0) ? (' · ' + d) : '') + loop, '内容 ' + ovPeek(a.content)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '定时注入' + ovStat(r);
        }
        var d = ovDurText(h.meta);
        var loop = (h.meta.loop === true) ? ' · 循环' : '';
        var clock = ovClock(ovMetaNum(h.meta, 'dueAt', 0));
        return '定时注入 · ' + ((d.length > 0) ? d : '待注入') + loop + ((clock.length > 0) ? (' · 到点 ' + clock) : '');
    }
});
