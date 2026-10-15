// ═══════════════════════════════════════════
// chat/tools/py/py-list.js —— py-list / 可用 py 脚本清单（PyCat）
// 声明：图标 🐍 + 输入意图行；段结构归骨架 `listing`。
// ═══════════════════════════════════════════

toolDecl('py-list', {
    icon: '🐍',
    inputLines: function (a) {
        var dir = ovText(a.dir);
        return [(dir.length > 0) ? ('列出 ' + dir + ' 下的 py 脚本') : '列出可用 py 脚本'];
    }
});
