// ═══════════════════════════════════════════
// chat/state/info.js —— 顶栏信息位投影（A194 自 state.js 迁出）
//
// 输入：state 段 `sessionId`（会话标识）· `tokens`（`count` 前文条数 / `context` 前文长度）
// 输出：#chatInfo——「前文 N 条 | sessionId=… | 前文 M tokens | 关键信息」（经信息位单点 `chatInfoSet`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `info` 行）
// 归位（A179）：告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本条信息位是 state 段专属
// 三段入口：条数 → `list` · tokens → `tokens` · 关键信息 → `key`——经 `data-ctx` 标注，点击开前文弹层
//           （委托处理归 `js/chat/ctx.js`；本件只出内容与标注）
// ═══════════════════════════════════════════

/// 头部数字——前文条数 / sessionId / 前文长度 / 关键信息（请求级最新值）；经信息位单点写（chatInfoSet——state 段专属）
/// @param {object} st state 段整段（分发器传入；只读 sessionId / tokens）
function stateRenderInfo(st) {
    var t = st.tokens || {};
    var parts = [];
    parts.push('前文 ');
    parts.push(stateCtxLink(fmtCount(t.count || 0) + ' 条', 'list', '前文条目——点击查看（按条 / 按 tokens / 关键信息 / 完整前文）'));
    parts.push(' | sessionId=' + st.sessionId);
    if (t.context > 0) {
        parts.push(' | ');
        parts.push(stateCtxLink('前文 ' + fmtCount(t.context) + ' tokens', 'tokens', '前文长度——点击查看 token 分布（按字符占比估算）'));
    }
    parts.push(' | ');
    parts.push(stateCtxLink('关键信息', 'key', '本次会话关键信息——加载报告 / 用户消息 / 正式回复 / 轮结算'));
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(parts);
    }
}

/// 信息位可点击段——`data-ctx` 标注目标视图（本件只出内容与标注，点击处理归 ctx.js）
/// @param {string} text 段文本
/// @param {string} mode 目标视图（list / tokens / key）
/// @param {string} title 悬停说明
function stateCtxLink(text, mode, title) {
    var node = elText('span', 'ctx-link', text);
    node.setAttribute('data-ctx', mode);
    node.title = title;
    return node;
}
