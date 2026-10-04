// ═══════════════════════════════════════════
// chat/blocks/think-sse.js —— thinksse（live 类 · 思考流）
//
// 契约（design-ch4-protocol §12.4 / §12.5）：
//   item = { type:'thinksse', payload:{ text } }
//   text = 该流当前的全部文本（live 段按帧全量镜像——后端给累计全文，前端只管渲染）
//   形态：.chat-think.stream（恒展开 + 尾部光标；行语义 / 刻度见 registry.js BLOCK_DECL）
//
// 纯渲染：payload → 行元素一次成型——无句柄、无判态、无统计无计数（计数类信息归后端）。
// ═══════════════════════════════════════════

/// live 段入口——thinksse：全量镜像一次到位（registry.js 的 LIVE_RENDERERS 消费）
function liveThinkSse(payload) {
    var p = payload || {};
    var row = blockRow('thinksse');
    var box = el('div', 'chat-think stream');

    var head = el('div', 'ct-head');
    head.appendChild(elText('span', 'ct-label', 'Think'));
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
