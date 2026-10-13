// ═══════════════════════════════════════════
// blocks/roundsummary.js —— roundsummary 块（persist 类 · 轮末总结）
//
// 契约：
//   item = { type:'roundsummary', ts, msgIndex, round, payload:{ text } }
//   来源：LLMSimpleRequest 服务产出一句话总结（20-50 字）；失败侧 text 为 ERR| 文本（失败可见）
//   位置：固定插在轮末统计（roundsum）之后
//
// 产出：.chat-row.assistant.summary > .chat-bubble.roundsummary > .rsm-head + .rsm-text
//   （形态声明见 registry.js BLOCK_DECL）
//
// 安全：用户/模型文本一律 textContent 写入（不自拼 HTML——防注入与子元素销毁）
// ═══════════════════════════════════════════

function buildRoundSummaryNoteBlock(payload) {
    var row = blockRow('roundsummary');
    var bubble = el('div', bodyClass('roundsummary'));
    var head = el('div', 'rsm-head');
    head.textContent = '📝 总结';
    bubble.appendChild(head);
    var body = el('div', 'rsm-text');
    body.textContent = (payload && payload.text) ? payload.text : '';
    bubble.appendChild(body);
    row.appendChild(bubble);
    return row;
}
