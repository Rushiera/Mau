// ═══════════════════════════════════════════
// chat/state/conn.js —— 连接健康投影（A194 自 state.js 迁出）
//
// 输入：state 段 `conn`（`server`: ok / stopping · `clients`: 当前 SSE 连接数——A186）
// 输出：#chatStatus 尾标（🔄 重启中 / 👥 N；皆不成立 → 零标记）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `conn` 行）
// 🔴 边界（契约 §12.2 ①）：本字段**不承担断线可见性**——断线后帧送不达，断线态归外观层本地信号
//    （`main.js` 的 `chatSse.onerror` → fx/pet 离线态）；重连自愈 = 服务端重推全量首帧。
// ═══════════════════════════════════════════

/// 连接健康投影——状态条尾部单标（幂等：先移除旧标再追加；状态条重建时旧标已被清，此处兜底防累积）
/// @param {object} st state 段整段（分发器传入；只读 conn）
function stateRenderConn(st) {
    var bar = document.getElementById('chatStatus');
    if (!bar) {
        return;
    }
    var old = document.getElementById('chatConn');
    if (old && old.parentNode) {
        old.parentNode.removeChild(old);
    }
    var c = st.conn;
    if (!c) {
        return;
    }
    var txt = '';
    if (c.server === 'stopping') {
        txt = '🔄 重启中';
    } else if (c.clients > 1) {
        txt = '👥 ' + c.clients;
    }
    if (txt.length === 0) {
        return;
    }
    var span = document.createElement('span');
    span.className = 'st conn';
    span.id = 'chatConn';
    span.textContent = txt;
    bar.appendChild(span);
}
