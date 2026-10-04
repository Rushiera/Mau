// ═══════════════════════════════════════════
// chat/state.js —— 状态段分发薄层（契约 §12.2 ①）
//
// 定位：state 段的「整段覆盖 + 按表分发」。state = **后端权威业务态整段**（每帧整段覆盖，非增量字段），
//       前端零推断：状态位与数字就位即渲染，不自行计时、不自行推算。
//
// 分层（A193 骨架 / A194 迁出）：分发表 = `state/registry.js::STATE_DECL`——投影件 → 消费字段 / 输出面 /
//       入口的**唯一映射处**；投影件分居 `state/`（status · info · note · delay · conn）与
//       `fx/`（controls · pet——外观层派生，A176 独立功能面）。
//       本件只剩「整段覆盖 + 按表分发」——**新增字段 = 加一件 + 表加一行，本件不动**。
//
// 字段面（后端 ChatSession.BuildStateJson）：
//   { sessionId, runState, runMs{idle,wait,link,think,tool,run,reply}, requests, note{…},
//     delay{entries[…]}, conn{server,clients},
//     tokens{prompt, completion, cacheHit, context, count, sessionPrompt, sessionCompletion, sessionCacheHit} }
//
// 观察项：按钮可用性属**外观层派生**（A184 改写契约），派生处 = fx/controls；delay 段（A185）为定时面板
//         数据源（原 GET /api/v1/delay 旁路端点退役，面板退化为纯显示）；conn 段（A186）为**连接健康**
//         （服务端视角：重启停机中 / 多页面连接数）——它补不了断线可见性（断线后收不到帧），
//         断线态仍由本地 SSE 信号派生（§12.8 pet 行）；重连自愈 = 全量首帧。
// ═══════════════════════════════════════════

/// 当前状态——state 段整段投影（唯一状态源的前端副本；分发器按表传给各投影件）
var appState = {
    sessionId: '',
    runState: '',
    runMs: {},
    requests: 0,
    note: null,
    delay: null,
    conn: null,
    tokens: {}
};

/// 状态段应用——整段覆盖后按分发表分发（入口与顺序的唯一处 = `state/registry.js::STATE_DECL`）
/// @param {object} st state 段整段（帧到达时三段之一；缺省字段回落默认值）
function stateApply(st) {
    if (!st) {
        return;
    }
    appState = {
        sessionId: st.sessionId || '',
        runState: st.runState || '',
        runMs: st.runMs || {},
        requests: st.requests || 0,
        note: st.note || null,
        delay: st.delay || null,
        conn: st.conn || null,
        tokens: st.tokens || {}
    };
    stateProjectAll(appState);
}
