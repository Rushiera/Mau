// CH4 外观层——chat-think.js：Think 块两态渲染（A85）
// 定位：think 块内部渲染的唯一实现——流式态（live-only：首帧新建、整块到达即销毁；不进视图层、不落盘）
//       + 完成态（随 reason 视图块落盘 / 历史重建）；头行统计口径见 design-ch4-frontend-theme §六「Think 块」
// 纪律：纯函数、无状态、无网络、无 innerHTML（文本一律 textContent）；时刻与时长由 chat-core.js 注入
// 加载顺序：chat-md.js → chat-cmd.js → chat-tools.js → chat-think.js → chat-view.js → chat-core.js（chat.html 引导层）

// ═══════════════════════════════════════════
// 完成态高度档——压缩（缩略摘要，默认）⇄ 展开（全文 500px 封顶 + 溢出滚动）
// 🔴 不是 details 的展开/折叠语义——同一元素上的类切换（.chat-think.done.full）；
//    流式态恒展开显示（不参与切换——内容在增长，压缩无意义）
// ═══════════════════════════════════════════

function chatThinkToggle(el) {
    if (!el) { return; }
    if (el.classList.contains('full')) {
        el.classList.remove('full');
        return;
    }
    el.classList.add('full');
}

// 高度档切换绑定——点击块内任意位置（死区判据与工具卡同源：位移 <20px 且 <300ms；排除交互元素与拖选文本）
// 依赖 chat-view.js 的 chatHasSelection / CHAT_COLLAPSE_MAX_MOVE / CHAT_COLLAPSE_MAX_MS（运行时解析）
function chatBindThinkToggle(el) {
    el.addEventListener('mousedown', function (e) {
        el._press = { x: e.clientX || 0, y: e.clientY || 0, t: Date.now(), sel: chatHasSelection() };
    });
    el.addEventListener('click', function (e) {
        var t = e.target;
        if (t && typeof t.closest === 'function') {
            if (t.closest('button, a, input, textarea, select')) { return; }
        }
        var p = el._press;
        if (p) {
            el._press = null;
            if (p.sel === true) { return; }
            if (Math.abs((e.clientX || 0) - p.x) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if (Math.abs((e.clientY || 0) - p.y) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if ((Date.now() - p.t) > CHAT_COLLAPSE_MAX_MS) { return; }
        }
        chatThinkToggle(el);
    });
}

// ═══════════════════════════════════════════
// 头行统计——「Think · 1.2k 字符 · 34 行 · 12.3s · 96 字符/s」
// ═══════════════════════════════════════════

// 统计取值——正文文本 → {chars, lines, ms}
// ms 非数值 = 无统计来源（历史重建 / 刷新重建——落盘载荷只有 content）→ 头行跳过时间与速率位并标识「未统计」
function chatThinkStats(text, ms) {
    var t = (typeof text === 'string') ? text : '';
    return {
        chars: t.length,
        lines: (t.length === 0) ? 0 : t.split('\n').length,
        ms: (typeof ms === 'number' && ms >= 0) ? ms : null
    };
}

// 速率——字符 / 秒（取整）；已输出 < 1s 不显示（分母无意义）；不做 token 换算（流式期前端无 token 数据）
function chatThinkRate(chars, ms) {
    if (typeof ms !== 'number' || ms < 1000) { return ''; }
    return Math.round(chars / (ms / 1000)) + ' 字符/s';
}

// 头行文本——单一拼装出口（流式态实时刷新 / 完成态一次成型共用）
function chatThinkHeadText(stats) {
    var s = stats || {};
    var parts = ['Think', chatFmtCount(s.chars || 0) + ' 字符', (s.lines || 0) + ' 行'];
    if (s.ms === null || s.ms === undefined) {
        parts.push('未统计');
    } else {
        parts.push(chatFmtMs(s.ms));
        var rate = chatThinkRate(s.chars || 0, s.ms);
        if (rate.length > 0) { parts.push(rate); }
    }
    return parts.join(' · ');
}

// 头行填充——标签与统计分色（文本来源唯一：chatThinkHeadText）
function chatThinkHeadFill(head, stats) {
    if (!head) { return; }
    var text = chatThinkHeadText(stats);
    var cut = text.indexOf(' · ');
    head.textContent = '';
    var label = document.createElement('span');
    label.className = 'ct-label';
    label.textContent = (cut < 0) ? text : text.substring(0, cut);
    head.appendChild(label);
    if (cut < 0) { return; }
    var stat = document.createElement('span');
    stat.className = 'ct-stat';
    stat.textContent = text.substring(cut);
    head.appendChild(stat);
}

// 头行刷新——按当前正文与已输出时长重算（1 秒表驱动；从时刻重算，非累加）
function chatThinkHeadRefresh(head, elapsedMs) {
    var box = head ? head.parentNode : null;
    var body = box ? box.querySelector('.ct-body') : null;
    chatThinkHeadFill(head, chatThinkStats(chatThinkBodyText(body), elapsedMs));
}

// ═══════════════════════════════════════════
// 正文——流式追加 / 取值 / 自动跟随
// ═══════════════════════════════════════════

// 流式正文取值——专用文本节点（光标是 body 末子元素，不参与统计）
function chatThinkBodyText(body) {
    if (!body || !body.textNode) { return ''; }
    return body.textNode.data;
}

// 流式正文追加——专用文本节点插在光标之前（textContent 拼接会销毁光标子元素）
function chatThinkAppend(body, cursor, text) {
    if (!body || !cursor) { return; }
    if (!body.textNode) {
        body.textNode = document.createTextNode('');
        body.insertBefore(body.textNode, cursor);
    }
    body.textNode.data = body.textNode.data + (text || '');
}

// 流式正文自动跟随——贴近底部才滚（用户上翻读历史时不打扰）
function chatThinkScroll(body) {
    if (!body) { return; }
    var near = body.scrollHeight - body.scrollTop - body.clientHeight;
    if (near >= 40) { return; }
    body.scrollTop = body.scrollHeight;
}

// ═══════════════════════════════════════════
// 两态构造——流式态（live-only）/ 完成态（落盘）
// ═══════════════════════════════════════════

// 压缩档内容——与旧「折叠摘要」同形：<3 行不压缩直接显示原文；≥3 行取 首行 +（N行M字符已省略显示）+ 末行
function chatThinkPeek(text) {
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) { return text; }
    var first = lines[0];
    var last = lines[lines.length - 1];
    var midLines = lines.length - 2;
    var midText = lines.slice(1, lines.length - 1).join('\n');
    return first + '（' + midLines + '行' + midText.length + '字符已省略显示）' + last;
}

// 流式态——头行 + 正文（恒展开：500px 封顶 + 溢出滚动）+ 尾部光标
// 起点由调用方写入头行 data-start（chat-core.chatLiveMark）；不参与高度档切换（内容在增长）
function chatThinkStream() {
    var box = document.createElement('div');
    box.className = 'chat-think stream';
    var head = document.createElement('div');
    head.className = 'ct-head';
    chatThinkHeadFill(head, chatThinkStats('', 0));
    box.appendChild(head);
    var body = document.createElement('div');
    body.className = 'ct-body';
    var cursor = document.createElement('span');
    cursor.className = 'ct-cursor';
    cursor.textContent = '▌';
    body.appendChild(cursor);
    box.appendChild(body);
    return { box: box, head: head, body: body, cursor: cursor };
}

// 完成态——头行 + 两档正文（压缩档为默认：缩略摘要；展开档：全文 500px 滚动）
// 两档内容同置 DOM，显示切换纯 CSS（点击只切类，零内容重建）
// stats 省略 / null → 由正文现算且标识「未统计」（历史重建无时长来源）
function chatThinkBlock(text, stats) {
    var content = (typeof text === 'string') ? text : '';
    var box = document.createElement('div');
    box.className = 'chat-think done';
    var head = document.createElement('div');
    head.className = 'ct-head';
    chatThinkHeadFill(head, stats || chatThinkStats(content, null));
    box.appendChild(head);
    var body = document.createElement('div');
    body.className = 'ct-body';
    var peek = document.createElement('div');
    peek.className = 'ct-peek';
    peek.textContent = chatThinkPeek(content);
    body.appendChild(peek);
    var full = document.createElement('div');
    full.className = 'ct-full';
    full.textContent = content;
    body.appendChild(full);
    box.appendChild(body);
    chatBindThinkToggle(box);
    return box;
}
