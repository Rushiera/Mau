// ═══════════════════════════════════════════
// chat/tools/py/py-service.js —— py-service / py 服务启停与状态（PyCat）
// 声明：图标 🐍 + 输入意图行（动作 / 服务名）；段结构归骨架 `json`。
// ═══════════════════════════════════════════

toolDecl('py-service', {
    icon: '🐍',
    inputLines: function (a) {
        return ['py 服务 ' + ovText(a.action) + ' · ' + ovText(a.name)];
    }
});
