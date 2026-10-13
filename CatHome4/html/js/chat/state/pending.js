// ═══════════════════════════════════════════
// chat/state/pending.js —— 插话队列投影（A233 自 fx/pending 迁入 state 面）
//
// 输入：state 段 `pending`（`entries[]`——后端 `ChatSession.BuildPendingFragment` 投影人工队列 `_pending`）
// 输出：#chatPendingPanel（⏳ 逐条；空表清空面板——`:empty` 自动隐藏）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `pending` 行）
//
// 语义（A233）：数据源改为**内核待处理队列投影**——覆盖全部来源（前端输入 / QQ 注入 / 指令 / 定时转投）；
//   原 `fx/pending.js` 的「本地在途记录」退役（内核队列是其超集：本地窗口 = 发送 → 内核受理的数十毫秒，
//   内核队列窗口 = 受理 → 进前文）。来源标记：QQ 注入（origin 非空）→ `[QQ]`；其余非 user 来源 → `[<source>]`。
// ═══════════════════════════════════════════

/// 来源标记——非前端来源可辨（QQ / systemauto / sleep / timer / delay / restart；前端输入无标记）
/// @param {object} e 队列条目（state 段 pending.entries 元素——{content, source, origin}）
/// @returns {string} 前缀标记（含尾随空格；无标记时空串）
function pendingSourceTag(e) {
    if (e.origin && e.origin.length > 0) {
        return '[QQ] ';
    }
    if (e.source && e.source.length > 0 && e.source !== 'user') {
        return '[' + e.source + '] ';
    }
    return '';
}

/// 插话队列投影——state 段 pending 段整段渲染（空表清空面板；容器缺失时零动作）
/// @param {object} st state 段整段（分发器传入；只读 pending）
function stateRenderPending(st) {
    var panel = document.getElementById('chatPendingPanel');
    if (!panel) {
        return;
    }
    var entries = (st && st.pending && st.pending.entries) ? st.pending.entries : [];
    if (entries.length === 0) {
        panel.textContent = '';
        return;
    }
    var html = '';
    for (var i = 0; i < entries.length; i = i + 1) {
        var e = entries[i] || {};
        html = html + '<div>⏳ ' + pendingSourceTag(e) + mdEscapeHtml(e.content || '') + '</div>';
    }
    panel.innerHTML = html;
}
