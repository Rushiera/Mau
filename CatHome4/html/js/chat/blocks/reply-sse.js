// ═══════════════════════════════════════════
// chat/blocks/reply-sse.js —— replysse（live 类 · 回复流）
//
// 契约（design-ch4-protocol §12.5）：
//   live 段 = { type:'replysse', context }（A196 状态投影——后端给该流当前全集，前端只管渲染）
//   形态：朴素件 + 流式语义类（行语义 / 刻度见 registry.js BLOCK_DECL）
//
// 纯渲染（A187）：payload → 行元素一次成型——不持句柄、不判终态、不配对；
//   条目从段内移出即不再渲染（live 段是运行态镜像，不是内容承载）。
//
// 来源：旧 stream-text.js（buildStreamText + setStreamText 句柄式入口——A187 收窄为纯渲染）
// ═══════════════════════════════════════════

/// live 段入口——replysse：全量镜像一次到位写入全文（registry.js 的 LIVE_RENDERERS 消费）
function liveReplySse(payload) {
    var p = payload || {};
    var row = blockRow('replysse');
    var bubble = el('div', bodyClass('replysse'));
    var text = (p.text === undefined || p.text === null) ? '' : String(p.text);
    if (text.length > 0) {
        bubble.appendChild(document.createTextNode(text));
    }
    row.appendChild(bubble);
    return row;
}
