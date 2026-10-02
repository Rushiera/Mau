// ═══════════════════════════════════════════
// blocks/stream-reason.js —— stream.reason（live 类 · 思考流式）
//
// 契约：
//   item = { type:'stream.reason', payload:{ kind:'reasoning', text } }
//   text = 当前该流式块的全部文本
//
// 用法（两态）：
//   var h = buildStreamReason();            // 造块（首帧一次）
//   appendStreamReason(h, chunk);           // 追加增量
//   thinkHeadRefresh(h.head, h.body, ms);   // 头行刷新（lib/think-head.js；时刻由主干注入）
//
// 产出：.chat-row.assistant.reason > .chat-think.stream > .ct-head + .ct-body >（文本节点 + .ct-cursor）
//
// 来源：chat-think.js 流式态（chatThinkStream / chatThinkAppend / chatThinkScroll）+ chat-core.js
//       chatOnLiveStream 的 reasoning 分支（第 423-438 行）
//
// 丢弃（属主干，不入素材）：
//   · 容器键表（viewContainers）、起点登记（chatLiveMark —— data-start 写元素）、
//     1 秒计时表（chatLiveTick）、页面可见性停表 —— 时刻注入与表驱动归主干
//   · 终结判定（原由后端 live.remove 驱动，新契约同样由主干按 live 镜像收敛）
// ═══════════════════════════════════════════

function buildStreamReason() {
    // 流式思考块——头行 + 正文（恒展开：500px 封顶 + 溢出滚动）+ 尾部光标
    // 🔴 流式态不参与高度档切换（内容在增长，压缩无意义）
    var row = el('div', 'chat-row assistant reason');
    var box = el('div', 'chat-think stream');

    var head = el('div', 'ct-head');
    thinkHeadFill(head, thinkStats('', 0));
    box.appendChild(head);

    var body = el('div', 'ct-body');
    var cursor = elText('span', 'ct-cursor', '▌');
    body.appendChild(cursor);
    box.appendChild(body);

    row.appendChild(box);
    return { row: row, box: box, head: head, body: body, cursor: cursor };
}

function appendStreamReason(h, chunk) {
    // 流式正文追加——专用文本节点插在光标之前（textContent 拼接会销毁光标子元素）
    if (!h || !h.body || !h.cursor) { return; }
    var text = (chunk === undefined || chunk === null) ? '' : String(chunk);
    if (text.length === 0) { return; }
    var body = h.body;
    if (!body.textNode) {
        body.textNode = document.createTextNode('');
        body.insertBefore(body.textNode, h.cursor);
    }
    body.textNode.data = body.textNode.data + text;
    scrollStreamReason(body);
}

function scrollStreamReason(body) {
    // 流式正文自动跟随——贴近底部才滚（用户上翻读历史时不打扰）
    if (!body) { return; }
    var near = body.scrollHeight - body.scrollTop - body.clientHeight;
    if (near >= 40) { return; }
    body.scrollTop = body.scrollHeight;
}
