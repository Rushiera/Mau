// ═══════════════════════════════════════════
// chat/state/meta.js —— 会话标识投影（A201 前端轮）
//
// 输入：state 段 `meta`（`displayName`——会话显示名；`session.new` 时按 cat.cfg 现值刷新）
// 输出：`document.title`（浏览器标题）+ `#chatTitle`（左上角）——同一 displayName 两处呈现
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `meta` 行）
// 边界：空 displayName 零动作（不写空标题——保留页面自带标题，失败可辨）
// ═══════════════════════════════════════════

/// 会话标识投影——浏览器标题与左上角显示名（幂等：同值重复写入无副作用）
/// @param {object} st state 段整段（分发器传入；只读 meta）
function stateRenderMeta(st) {
    var m = st.meta;
    if (!m || !m.displayName) {
        return;
    }
    var name = m.displayName;
    if (document.title !== name) {
        document.title = name;
    }
    var el = document.getElementById('chatTitle');
    if (el && el.textContent !== name) {
        el.textContent = name;
    }
}
