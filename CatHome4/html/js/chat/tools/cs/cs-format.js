// ═══════════════════════════════════════════
// chat/tools/cs/cs-format.js —— cs-format / 空白缩进规整（CsCat）
// 声明：折叠行读结构化头（mode / changedFiles / changedLines / failedFiles）+ 输入意图行；
//       段结构归骨架 `diagnostics`（check 差异清单逐行）。
// ═══════════════════════════════════════════

toolDecl('cs-format', {
    inputLines: function (a) {
        var mode = (a.mode === 'apply') ? 'apply' : 'check';
        return ['格式' + (mode === 'apply' ? '规整' : '检查') + ' ' + ovText(a.path) + ' · mode=' + mode];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '格式哨兵 ' + ovShort(a.path) + ovStat(r);
        }
        var m = h.meta;
        var act = (m.mode === 'apply') ? '规整' : '需规整';
        var failed = ovMetaNum(m, 'failedFiles', 0);
        var tail = (failed > 0) ? (' · ' + failed + ' 文件校验未过') : '';
        return '格式哨兵 ' + ovShort(a.path) + ' · ' + act + ' ' + ovMetaNum(m, 'changedFiles', 0) + ' 文件 / ' + ovMetaNum(m, 'changedLines', 0) + ' 行' + tail;
    }
});
