// ═══════════════════════════════════════════
// chat/fx/controls.js —— 动作按钮态（独立功能件 A176）
//
// 定位：按 state 段的 runState **派生动作按钮可用性**（停止 / 继续 / 开始 Note / 发送）。
// 输入：state 段的 runState（只读原始信息）
// 输出：#chatPause · #chatContinue · #noteStartBtn · #chatSendBtn · #chatSendInput 的 disabled 态
// 判据：外观层允许推算（设计 §1.1）——按钮显隐 / 禁用属合法推算；不产生业务效果、不含指令输入
// 说明：契约 §12.2 ① 曾把「按钮可用性」列为 state 段字段——A184 改写为外观层派生（本件即该派生处）
// 登记：fx/registry.js 的 FX_FEATURES（契约 §12「独立功能面」节）
// ═══════════════════════════════════════════

/// 通用禁用设置——元素缺失即跳过（防御式）
function setDisabled(id, disabled) {
    var node = document.getElementById(id);
    if (node) {
        node.disabled = (disabled === true);
    }
}

/// 轮进行中判据——runState 非空且非 idle（后端权威态，前端不猜）
function fxControlsRunning(state) {
    var s = (state && state.runState) ? state.runState : '';
    if (s === '' || s === 'idle') {
        return false;
    }
    return true;
}

/// 应用——按轮进行态派生按钮可用性（发送恒可用；忙时插话走插话队列语义）
function fxControlsApply(state) {
    var running = fxControlsRunning(state);
    setDisabled('chatPause', !running);
    setDisabled('chatContinue', running);
    setDisabled('noteStartBtn', running);
    setDisabled('chatSendBtn', false);
    setDisabled('chatSendInput', false);
}
