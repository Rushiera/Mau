// ═══════════════════════════════════════════
// chat/state/status.js —— 状态条投影（A194 自 state.js 迁出）
//
// 输入：state 段 `runState`（当前态高亮）· `runMs`（六态时长 + ⏱ 总）· `requests`（🔄 Api 次数）
// 输出：#chatStatus——六态 spans + ⏱ 总 + Api 段（**本件是容器的唯一写者**，整条重建）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `status` 行）
// 六态元信息：`registry.js::RUN_PHASES`（A179 收口——与轮末统计共用一份，后端 runMs 键名对齐）
// ═══════════════════════════════════════════

/// 状态条——六态完成后端时长（当前态高亮）+ ⏱ 总 + 请求次数；无数据整行空
/// @param {object} st state 段整段（分发器传入；只读 runState / runMs / requests）
function stateRenderStatus(st) {
    var bar = document.getElementById('chatStatus');
    if (!bar) {
        return;
    }
    var ms = st.runMs || {};
    var active = st.runState || '';
    var html = '';
    var total = 0;
    for (var i = 0; i < RUN_PHASES.length; i++) {
        var p = RUN_PHASES[i];
        var v = ms[p.key] || 0;
        total = total + v;
        if (v <= 0 && active !== p.key) {
            continue;
        }
        var cls = (active === p.key) ? ' active' : '';
        html += '<span class="st ' + p.key + cls + '">' + p.icon + ' ' + p.label + ' ' + fmtMs(v) + '</span>';
    }
    if (total > 0) {
        html += '<span class="st total">⏱ All ' + fmtMs(total) + '</span>';
    }
    if (st.requests > 0) {
        var api = '🔄 Api ' + st.requests;
        if ((ms.link || 0) > 0) {
            api += '（' + (ms.link / 1000 / st.requests).toFixed(2) + ' s /use）';
        }
        html += '<span class="st req">' + api + '</span>';
    }
    bar.innerHTML = html;
}
