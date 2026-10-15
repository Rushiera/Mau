// ═══════════════════════════════════════════
// chat/tools/py/py-list.js —— py-list / 可用 py 脚本清单（PyCat）
// 声明：图标 📜 + 输入意图行（目录）+ 折叠行与输出自然语言化；段结构归骨架 `listing`。
// 回执形态（PyService.List）：{count, scripts:[脚本绝对路径]}
// ═══════════════════════════════════════════

toolDecl('py-list', {
    icon: '🐍',
    inputLines: function (a) {
        var dir = (typeof a.dir === 'string') ? a.dir : '';
        return [(dir.length > 0) ? ('列出 ' + dir + ' 下的 py 脚本') : '列出可用 py 脚本'];
    },
    headline: function (a, r) {
        if (r === undefined) {
            return '列出 py 脚本 · 扫描中';
        }
        var o = ovJson(r);
        if (!o) {
            return '列出 py 脚本' + ovStat(r);
        }
        return '列出 py 脚本 · ' + ovMetaNum(o, 'count', 0) + ' 个';
    },
    badge: function (r) {
        var o = ovJson(r);
        if (!o) {
            return '';
        }
        return ' · ' + ovMetaNum(o, 'count', 0) + ' 个脚本';
    },
    outputLines: function (r, a, isErr) {
        if (isErr) {
            return null;
        }
        if (r === undefined) {
            return ['扫描中…'];
        }
        var o = ovJson(r);
        if (!o || !Array.isArray(o.scripts)) {
            return null;
        }
        if (o.scripts.length === 0) {
            return ['（无 py 脚本）'];
        }
        var out = [];
        for (var i = 0; i < o.scripts.length; i = i + 1) {
            out.push(String(o.scripts[i]));
        }
        return out;
    }
});
