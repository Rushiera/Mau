// ═══════════════════════════════════════════
// chat/blocks/think-sse.js —— thinksse（live 类 · 思考流）
//
// 契约（design-ch4-protocol §12.5）：live 段 = { type:'thinksse', context }（A196 状态投影）
//   context = 该流当前整段全文——后端给全集，前端整段替换。
//
// 形态（2026-10-06 莎定 · 临时最小态）：**无渲染过程**——原始文本直投面板：
//   不套壳、不建结构、无头行、无计数。live 区形态重做归后续专项。
// 🔴 写入点约定（stream.js 白名单挂载件——整套流式效果的单点承载处）：**块体内首个文本节点**即打字机写入点——
//   本件空文本亦建出文本节点（走步逐帧写 node.data）；进行中光标由 stream.js 挂 `.streaming`（CSS 单点出 ▌）。
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
