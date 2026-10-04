// ═══════════════════════════════════════════
// blocks/reason.js —— reason 块（persist 类 · 完成态思考块）
//
// 契约：
//   item = { type:'reason', ts, msgIndex, round, payload:{ text } }
//
// 产出：.chat-row.assistant.reason > .chat-think.done
//         ├ .ct-head（标签行）
//         └ .ct-body ├ .ct-peek 压缩档（默认显示）
//                    └ .ct-full 展开档（点击整块切换 .full 类，纯 CSS 换显）
//
// 纯渲染：只渲染正文——时长 / 行数 / 字符数等计数类信息归后端（前端不算、不标）。
// ═══════════════════════════════════════════

function thinkPeek(text) {
    // 压缩档内容——<3 行不压缩直接显示原文；≥3 行取首行 + 末行（中段省略）
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) { return text; }
    return lines[0] + '\n……\n' + lines[lines.length - 1];
}

function buildReasonBlock(payload) {
    // 完成态思考块——头行 + 两档正文（两档内容同置 DOM，显示切换纯 CSS：点击只切类，零内容重建）
    var p = payload || {};
    var content = (typeof p.text === 'string') ? p.text : '';

    var row = blockRow('reason');
    var box = el('div', 'chat-think done');

    var head = el('div', 'ct-head');
    head.appendChild(elText('span', 'ct-label', 'Think'));
    box.appendChild(head);

    var body = el('div', 'ct-body');
    body.appendChild(elText('div', 'ct-peek', thinkPeek(content)));
    body.appendChild(elText('div', 'ct-full', content));
    box.appendChild(body);

    bindPressToggle(box, thinkToggle);
    row.appendChild(box);
    return row;
}

function thinkToggle(box) {
    // 高度档切换——压缩（默认）⇄ 展开（全文 500px 封顶 + 溢出滚动）
    // 🔴 不是 details 的展开/折叠语义——同一元素上的类切换（.chat-think.done.full）
    if (!box) { return; }
    if (box.classList.contains('full')) {
        box.classList.remove('full');
    } else {
        box.classList.add('full');
    }
}
