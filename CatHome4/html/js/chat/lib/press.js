// ═══════════════════════════════════════════
// lib/press.js —— 按压死区判据（点按切换的通用判据）
// 来源：chat-view.js chatHasSelection / CHAT_COLLAPSE_MAX_MOVE / CHAT_COLLAPSE_MAX_MS / chatBindBodyCollapse
//       + chat-think.js chatBindThinkToggle（两处判据同源，此处收口为一处）
// 语义：展开态整块点击收起——原生 details 只有折叠头可点，展开后内容区长；
//       折叠头（summary）· 交互元素 · 按下时已有选区 · 按下→抬起位移/时长超死区 → 均不触发
// 取舍：保留判据；去掉 chat 前缀与两处重复实现
// ═══════════════════════════════════════════

// 死区阈值——按下→抬起的位移/时长上限；超限视为拖选或长按，不触发切换
var PRESS_MAX_MOVE = 20;
var PRESS_MAX_MS = 300;

function pressHasSelection() {
    // 按下时是否已有选区（拖选中的文本不能被点按其打断）
    if (typeof window.getSelection !== 'function') { return false; }
    var sel = window.getSelection();
    if (!sel || sel.isCollapsed === true) { return false; }
    return sel.toString().length > 0;
}

function bindPressToggle(target, onToggle, opts) {
    // 绑定「整块点按切换」——target 为承接点击的元素；opts.onlyWhenOpen = 仅展开态生效
    // 排除面：折叠头（summary）· 按钮/链接/输入类元素
    var onlyWhenOpen = !!(opts && opts.onlyWhenOpen);
    target.addEventListener('mousedown', function (e) {
        target._press = {
            x: e.clientX || 0,
            y: e.clientY || 0,
            t: Date.now(),
            sel: pressHasSelection()
        };
    });
    target.addEventListener('click', function (e) {
        if (onlyWhenOpen && target.open !== true) { return; }
        var t = e.target;
        if (t && typeof t.closest === 'function') {
            if (t.closest('summary') || t.closest('button, a, input, textarea, select')) { return; }
        }
        var p = target._press;
        if (p) {
            target._press = null;
            if (p.sel === true) { return; }
            if (Math.abs((e.clientX || 0) - p.x) > PRESS_MAX_MOVE) { return; }
            if (Math.abs((e.clientY || 0) - p.y) > PRESS_MAX_MOVE) { return; }
            if ((Date.now() - p.t) > PRESS_MAX_MS) { return; }
        }
        onToggle(target);
    });
}
