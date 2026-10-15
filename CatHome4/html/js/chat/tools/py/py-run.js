// ═══════════════════════════════════════════
// chat/tools/py/py-run.js —— py-run / 执行 py 脚本（PyCat）
// 声明：图标 🐍 + 输入意图行；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('py-run', {
    icon: '🐍',
    inputLines: function (a) {
        var args = ovText(a.args);
        var tail = (args.length > 0) ? (' ' + args) : '';
        return ['运行 py 脚本 ' + ovText(a.script) + tail];
    }
});
