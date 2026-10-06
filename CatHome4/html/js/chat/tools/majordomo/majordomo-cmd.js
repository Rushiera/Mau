// ═══════════════════════════════════════════
// chat/tools/majordomo/majordomo-cmd.js —— majordomo-cmd / 宿主管理指令（特权）
// 声明：折叠行读结构化头 + 输入意图行；段结构归骨架 `exec`。
// ═══════════════════════════════════════════

toolDecl('majordomo-cmd', {
    inputLines: function (a) {
        return ['宿主指令 ' + ovText(a.cmd)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '宿主指令' + ovStat(r);
        }
        return '宿主指令 ' + ovShort(a.cmd);
    }
});
