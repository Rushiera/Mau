// ═══════════════════════════════════════════
// chat/state/info.js —— 顶栏信息位投影（A194 自 state.js 迁出）
//
// 输入：state 段 `tokens`（`count` 前文条数 · `context` 前文长度 · `session*` 会话级消耗三项）
//       `meta`（`contextChars` 前文字符数）
// 输出：#chatInfo——「前文 N 条 X.XXK token（Y.YYK字符）|↑ … ↓ … miss … （🎯…%）」（经信息位单点 `chatInfoSet`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `info` 行）
// 归位（A179）：告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本条信息位是 state 段专属
// 格式化（A201）：统计数字一律走 `fmtCount`（两位小数 + K/M 进位）；条数为离散计数，整数直出
// ═══════════════════════════════════════════

/// 头部信息位——前文条数与长度、前文字符数、会话级（全局）token 消耗与命中率
/// 富文本分片（2026-10-06）：数值包 `.ci-num`（随正文高亮色）· 命中率包 `.ci-rate`（淡紫）；
///   片段 = 固定字面量 + `fmtCount` 数值（无用户文本）——组装侧无需转义
/// @param {object} st state 段整段（分发器传入；只读 tokens / meta）
function stateRenderInfo(st) {
    var t = st.tokens || {};
    var m = st.meta || {};
    var num = function (v) {
        return '<span class="ci-num">' + fmtCount(v) + '</span>';
    };
    // 条数为离散计数——整数直出（不走 fmtCount）；其余统计走 num() 分片
    var html = '前文 <span class="ci-num">' + (t.count || 0) + '</span>'
        + ' 条 ' + num(t.context) + ' token（' + num(m.contextChars) + '字符）'
        + '|↑ ' + num(t.sessionPrompt)
        + ' ↓ ' + num(t.sessionCompletion)
        + ' miss ' + num(t.sessionMiss)
        + ' （🎯<span class="ci-rate">' + (Number(t.sessionRate || 0) * 100).toFixed(2) + '%</span>）';
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(html);
    }
}
