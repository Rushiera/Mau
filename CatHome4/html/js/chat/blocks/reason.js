// ═══════════════════════════════════════════
// blocks/reason.js —— reason 块（persist 类 · 完成态思考块）
//
// 契约：
//   item = { type:'reason', ts, msgIndex, round, payload:{ text, durMs? } }
//   durMs = 落盘时长（可缺——无统计来源时头行标「未统计」）
//
// 产出：.chat-row.assistant.reason > .chat-think.done
//         ├ .ct-head（.ct-label 标签 + .ct-stat 统计）
//         └ .ct-body ├ .ct-peek 压缩档（默认显示）
//                    └ .ct-full 展开档（点击整块切换 .full 类，纯 CSS 换显）
//
// 来源：chat-think.js 完成态（chatThinkBlock / chatThinkPeek / chatThinkToggle / chatBindThinkToggle）
//
// 丢弃（属主干，不入素材）：
//   · 流式态（chatThinkStream —— 见 blocks/stream-reason.js）
//   · 活跃计时表（chatLiveMark / chatLiveTick / data-start —— 时刻由主干注入 durMs）
//   · 滚动带刻度重建（chatBandScheduleRebuild —— 属主轴）
//   · DOM 挂载 / 拖动死区常量（死区判据收口 lib/press.js）
// ═══════════════════════════════════════════

function thinkPeek(text) {
    // 压缩档内容——<3 行不压缩直接显示原文；≥3 行取 首行 +（N行M字符已省略显示）+ 末行
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) { return text; }
    var first = lines[0];
    var last = lines[lines.length - 1];
    var midLines = lines.length - 2;
    var midText = lines.slice(1, lines.length - 1).join('\n');
    return first + '（' + midLines + '行' + midText.length + '字符已省略显示）' + last;
}

function buildReasonBlock(payload) {
    // 完成态思考块——头行 + 两档正文（两档内容同置 DOM，显示切换纯 CSS：点击只切类，零内容重建）
    var p = payload || {};
    var content = (typeof p.text === 'string') ? p.text : '';
    var stats = (typeof p.durMs === 'number') ? thinkStats(content, p.durMs) : null;

    var row = blockRow('reason');
    var box = el('div', 'chat-think done');

    var head = el('div', 'ct-head');
    thinkHeadFill(head, stats || thinkStats(content, null));
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
