// ═══════════════════════════════════════════
// chat/state/note.js —— Note 投影（A194 自 state.js 迁出）
//
// 输入：state 段 `note`（后端权威 Note 状态——A191 后单源，`note` 事件已退役）
// 输出：Note 面板与气泡（`../note.js` 的 `noteRenderFromState`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `note` 行）
// ═══════════════════════════════════════════

/// Note 投影——转交面板件（件缺失时零动作；失败可见由分发器出声）
/// @param {object} st state 段整段（分发器传入；只读 note）
function stateRenderNote(st) {
    if (typeof noteRenderFromState === 'function') {
        noteRenderFromState(st.note);
    }
}
