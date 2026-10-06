// ═══════════════════════════════════════════
// chat/tools/cs/cs-patch.js —— cs-patch / 方法体级替换（CsCat）
// 声明：折叠行读结构化头（state / start / end）+ 输入意图行；输出段逐行自然语言化（落盘源码，剥行尾标注）。
// ═══════════════════════════════════════════

toolDecl('cs-patch', {
    inputLines: function (a) {
        return ['改写 ' + ovText(a.class) + '.' + ovText(a.method) + ' 方法体', '项目 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var target = ovText(a.class) + '.' + ovText(a.method);
        var h = ovHead(r);
        if (!h) {
            return '改写 ' + target + ovStat(r);
        }
        var m = h.meta;
        var start = ovMetaNum(m, 'start', -1);
        var range = (start > 0) ? (' · L' + start + '-' + ovMetaNum(m, 'end', start)) : '';
        var state = ovMetaStr(m, 'state');
        return '改写 ' + target + ' · ' + (state.length > 0 ? state : 'OK') + range;
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var body = h.body;
        if (typeof body !== 'string' || body.length === 0) {
            return ['已落盘 ' + ovMetaStr(h.meta, 'file')];
        }
        var lines = body.split('\n');
        var out = [];
        for (var i = 0; i < lines.length; i = i + 1) {
            out.push(stripLineMark(lines[i]));
        }
        return out;
    }
});
