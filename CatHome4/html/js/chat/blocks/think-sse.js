// ═══════════════════════════════════════════
// chat/blocks/think-sse.js —— thinksse（live 类 · 思考流）
//
// 契约（design-ch4-protocol §12.4 / §12.5）：
//   item = { type:'thinksse', payload:{ kind:'reasoning', text } }
//   text = 该流当前的全部文本（live 段按帧全量镜像）
//   形态：.chat-think.stream（恒展开 + 尾部光标；行语义 / 刻度见 registry.js BLOCK_DECL）
//
// 纯渲染（A187）：payload → 行元素一次成型——不持句柄、不判终态；
//   头行统计建块时写初始值（流式期不刷新——头行统计缺口记 A198 核对面）。
//
// 退役（A187）：旧件 buildStreamReason / appendStreamReason / scrollStreamReason 句柄式追随——
//   全量镜像路径每帧重建行元素，句柄与滚动追随无适用面。
// ═══════════════════════════════════════════

/// live 段入口——thinksse：全量镜像一次到位（registry.js 的 LIVE_RENDERERS 消费）
function liveThinkSse(payload) {
    var p = payload || {};
    var row = blockRow('thinksse');
    var box = el('div', 'chat-think stream');

    var head = el('div', 'ct-head');
    thinkHeadFill(head, thinkStats('', 0));
    box.appendChild(head);

    var body = el('div', 'ct-body');
    var text = (p.text === undefined || p.text === null) ? '' : String(p.text);
    if (text.length > 0) {
        body.appendChild(document.createTextNode(text));
    }
    body.appendChild(elText('span', 'ct-cursor', '▌'));
    box.appendChild(body);

    row.appendChild(box);
    return row;
}
