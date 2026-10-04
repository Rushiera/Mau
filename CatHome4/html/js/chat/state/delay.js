// ═══════════════════════════════════════════
// chat/state/delay.js —— 定时面板投影（A194 自 state.js 迁出）
//
// 输入：state 段 `delay`（`entries[]`——A185 入段后，`GET /api/v1/delay` 旁路端点退役）
// 输出：定时面板列表（`../delay.js` 的 `delayApplyState`——纯显示，写面走指令总线）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `delay` 行）
// ═══════════════════════════════════════════

/// 延迟面板投影——state 段 delay 段整段交给面板件（数据源入段后退化为纯显示；件缺失时零动作）
/// @param {object} st state 段整段（分发器传入；只读 delay）
function stateRenderDelay(st) {
    if (typeof delayApplyState === 'function') {
        delayApplyState(st.delay);
    }
}
