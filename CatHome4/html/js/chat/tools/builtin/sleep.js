// ═══════════════════════════════════════════
// chat/tools/builtin/sleep.js —— sleep / 登记定时唤醒（内置）
// 声明：专属图标 ⏳ + 折叠行（时长 + 到点时刻）+ 输入意图行；等待语义（主干被其他输入启动即作废）。
// ═══════════════════════════════════════════

toolDecl('sleep', {
    icon: '⏳',
    inputLines: function (a) {
        var d = ovDurText(a);
        return ['登记定时唤醒' + ((d.length > 0) ? (' · ' + d) : '')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '定时唤醒' + ovStat(r);
        }
        var d = ovDurText(h.meta);
        var clock = ovClock(ovMetaNum(h.meta, 'dueAt', 0));
        return '定时唤醒 · ' + ((d.length > 0) ? d : '待唤醒') + ((clock.length > 0) ? (' · 到点 ' + clock) : '');
    }
});
