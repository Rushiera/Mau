// ═══════════════════════════════════════════
// blocks/retry.js —— retry 块（persist 类 · LLM 重试过程记录）
//
// 契约：
//   item = { type:'retry', ts, msgIndex, round, payload:{ state, attempt, max, text } }
//   state = retrying（默认）/ resolved（原文 + ✓ 已恢复）/ failed（原文 + ⚠ 重试失败）
//   attempt / max = 第几次 / 上限；text = 原因原文（resolved 时不覆盖原文——原文保留 + 追加）
//
// 产出：.chat-row.assistant > .chat-bubble.retry[.resolved|.failed]
//
// 来源：chat-core.js chatRetryText / chatApplyRetryState / chatRenderRetry（第 477-505 行）
//
// 取舍：三态语义与文本拼装原样保留；气泡构造改走 el()；DOM 挂载与滚动跟随丢弃
// ═══════════════════════════════════════════

function chatRetryText(payload) {
    // 重试气泡文本（渲染单例内文本面）
    var p = payload || {};
    var state = p.state || 'retrying';
    var attempt = p.attempt || '';
    var max = p.max || '';
    var reason = p.text || '';
    var bar = attempt + (max ? '/' + max : '');
    var tail = reason ? (' · ' + reason) : '';
    if (state === 'failed') {
        return '⚠ 重试失败 ' + bar + tail;
    }
    return '⟳ 重试中 ' + bar + tail + (state === 'resolved' ? ' ✓ 已恢复' : '');
}

function applyRetryState(bubble, payload) {
    // 状态类单一出口（resolved / failed 终态；retrying 无附加类）
    var p = payload || {};
    var state = p.state || 'retrying';
    bubble.classList.toggle('resolved', state === 'resolved');
    bubble.classList.toggle('failed', state === 'failed');
}

function buildRetryBlock(payload) {
    // 重试气泡——历史重建与实时事件共用同一渲染面
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', 'chat-bubble retry');
    bubble.textContent = chatRetryText(payload);
    applyRetryState(bubble, payload);
    row.appendChild(bubble);
    return row;
}
