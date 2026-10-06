// ═══════════════════════════════════════════
// chat/tools/config/config-get.js —— config-get / 读配置项（ConfigCat）
// 声明：折叠行读结构化头（key / source / declared / writable）+ 输入意图行；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('config-get', {
    inputLines: function (a) {
        return ['读取配置 ' + ovText(a.key)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '读取配置 ' + ovText(a.key) + ovStat(r);
        }
        var m = h.meta;
        var src = ovMetaStr(m, 'source');
        var srcTail = (src.length > 0) ? (' · 来源 ' + src) : '';
        var ro = (m.declared === true && m.writable === false) ? ' · 只读' : '';
        return '读取配置 ' + ovMetaStr(m, 'key') + srcTail + ro;
    }
});
