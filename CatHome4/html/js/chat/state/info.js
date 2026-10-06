// ═══════════════════════════════════════════
// chat/state/info.js —— 顶栏信息位投影（A194 自 state.js 迁出）
//
// 输入：state 段 `tokens`（`count` 前文条数 · `context` 前文长度 · `session*` 会话级消耗三项）
//       `meta`（`contextChars` 前文字符数）
// 输出：#chatInfo——「前文 N 条。X.XXK token（Y.YYK字符）|↑ … ↓ … miss … （🎯…%）」（经信息位单点 `chatInfoSet`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `info` 行）
// 归位（A179）：告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本条信息位是 state 段专属
// 格式化（A201）：统计数字一律走 `fmtCount`（两位小数 + K/M 进位）；条数为离散计数，整数直出
// ═══════════════════════════════════════════

/// 头部信息位——前文条数与长度、前文字符数、会话级（全局）token 消耗与命中率
/// @param {object} st state 段整段（分发器传入；只读 tokens / meta）
function stateRenderInfo(st) {
    var t = st.tokens || {};
    var m = st.meta || {};
    var txt = '前文 ' + (t.count || 0) + ' 条。'
        + fmtCount(t.context) + ' token（' + fmtCount(m.contextChars) + '字符）'
        + '|↑ ' + fmtCount(t.sessionPrompt)
        + ' ↓ ' + fmtCount(t.sessionCompletion)
        + ' miss ' + fmtCount(t.sessionMiss)
        + ' （🎯' + (Number(t.sessionRate || 0) * 100).toFixed(2) + '%）';
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(txt);
    }
}
