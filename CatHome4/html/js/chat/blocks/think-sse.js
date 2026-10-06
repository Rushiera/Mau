// ═══════════════════════════════════════════
// chat/blocks/think-sse.js —— thinksse（live 类 · 思考流）
//
// 契约（design-ch4-protocol §12.5）：live 段 = { type:'thinksse', context }（A196 状态投影）
//   context = 该流当前整段全文——后端给全集，前端整段替换。
//
// 形态（2026-10-06 莎定 · 临时最小态）：**无渲染过程**——原始文本直投面板：
//   不套壳、不建结构、无头行、无光标、无计数。live 区形态重做归后续专项。
// ═══════════════════════════════════════════

/// live 段入口——thinksse：原始文本直投（registry.js 的 LIVE_RENDERERS 消费）
function liveThinkSse(payload) {
    var p = payload || {};
    var row = blockRow('thinksse');
    var body = el('div', formClass('thinksse'));
    var text = (p.text === undefined || p.text === null) ? '' : String(p.text);
    body.appendChild(document.createTextNode(text));
    row.appendChild(body);
    return row;
}
