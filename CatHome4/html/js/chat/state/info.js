// ═══════════════════════════════════════════
// chat/state/info.js —— 顶栏信息位投影（A194 自 state.js 迁出）
//
// 输入：state 段 `sessionId`（会话标识）· `tokens`（`count` 前文条数 / `context` 前文长度）
// 输出：#chatInfo——「前文 N 条 | sessionId=… | 前文 M tokens」（经信息位单点 `chatInfoSet`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `info` 行）
// 归位（A179）：告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本条信息位是 state 段专属
// ═══════════════════════════════════════════

/// 头部数字——前文条数 / sessionId / 前文长度（请求级最新值）
/// @param {object} st state 段整段（分发器传入；只读 sessionId / tokens）
function stateRenderInfo(st) {
    var t = st.tokens || {};
    var txt = '前文 ' + fmtCount(t.count || 0) + ' 条 | sessionId=' + st.sessionId;
    if (t.context > 0) {
        txt += ' | 前文 ' + fmtCount(t.context) + ' tokens';
    }
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(txt);
    }
}
