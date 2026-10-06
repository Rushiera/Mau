// ═══════════════════════════════════════════
// chat/tools/mau/mau-setup.js —— mau-setup / 一键部署链（MauCat）
// 声明：折叠行读结构化头（ok / mode / steps / stepsOk / artifacts）+ 输入意图行；
//       段结构归骨架 `exec`（无 command 键 → 输入段走通用键值表，输出段走原始输出）。
// ═══════════════════════════════════════════

toolDecl('mau-setup', {
    inputLines: function (a) {
        var mode = a.mode || 'prepare';
        var extra = a.target ? (' · 目标 ' + a.target) : '';
        return ['一键部署 ' + mode + extra];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '一键部署' + ovStat(r);
        }
        var m = h.meta;
        var base = '一键部署 ' + ovMetaStr(m, 'mode') + ' · ' + ovMetaNum(m, 'stepsOk', 0) + '/' + ovMetaNum(m, 'steps', 0)
            + ' 步 · ' + ovMetaNum(m, 'artifacts', 0) + ' 产物';
        if (m.ok === false) {
            return base + ' · 失败';
        }
        return base;
    }
});
