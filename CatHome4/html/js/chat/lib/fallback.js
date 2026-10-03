// ═══════════════════════════════════════════
// chat/lib/fallback.js —— 两区共用兜底件（A178 归位 · 原在 persist.js）
//
// 定位：渲染链的两个兜底出口——持久区与临时区共用（原定义在 persist.js 被 live.js 反向调用，
//       属跨件隐式加载顺序耦合；归位 lib 后两区各自引用，无反向依赖）。
//   ① buildUnknownBlock——未登记 type（前端渲染表无此 type）
//   ② buildRenderErrorBlock——单块渲染抛错（功能隔离：一个异常不中断整批）
// 判据：失败可见（AGENTS 底线四）——块不消失，显式告诉人不认识 / 坏了。
// ═══════════════════════════════════════════

/// 未识别块型兜底——默认报错气泡（type + 载荷摘要）
function buildUnknownBlock(item) {
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', formClass('error') + ' error');
    var payload = '';
    try {
        payload = JSON.stringify(item.payload || {});
    } catch (e) {
        payload = '[载荷不可序列化]';
    }
    if (payload.length > 200) {
        payload = payload.substring(0, 200) + '…';
    }
    bubble.textContent = '⚠ 未识别的块型「' + item.type + '」——前端渲染表无此 type；载荷：' + payload;
    row.appendChild(bubble);
    return row;
}

/// 渲染异常兜底——单块抛错时替代该块（**功能隔离**：一个异常不中断其他；异常可见，不静默丢块）
function buildRenderErrorBlock(item, err) {
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', formClass('error') + ' error');
    var msg = (err && err.message) ? err.message : String(err);
    bubble.textContent = '⚠ 块渲染失败「' + ((item && item.type) ? item.type : '?') + '」：' + msg + '——该块已跳过，其余不受影响';
    row.appendChild(bubble);
    return row;
}
