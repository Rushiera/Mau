// ═══════════════════════════════════════════
// blocks/reason.js —— reason 块（persist 类 · 完成态思考块）
//
// 契约（design-ch4-protocol §12.5）：
//   item = { type:'reason', ts, msgIndex, round,
//            payload:{ text, durMs, chars, cps } }
//   · text  = 思考正文
//   · durMs = 本块思考用时 ms（-1 = 未记录——载入顶尾补差等无实时数据路径）
//   · chars = 正文字符数；cps = 每秒字符数（durMs ≤ 0 时 -1）
//
// 产出：.chat-row.assistant.reason > .chat-bubble > .chat-think.done
//         ├ .ct-head（Think 标签 + 统计项——切档唯一热区）
//         └ .ct-body ├ .ct-peek 压缩档（默认显示）
//                    └ .ct-full 展开档（点击头行切 .full 类，纯 CSS 换显）
//
// 渲染口径（A199）：计数类全部取后端载荷——chars / durMs / cps 直用，前端不自算；
//   仅「行数」为显示派生（正文换行符计数——外观格式化，零业务效果）；
//   统计缺失（durMs 未记录）→ 时间与速率位不显示，尾附「未统计」。
// ═══════════════════════════════════════════

function thinkLineCount(text) {
    // 行数——显示派生（后端不给行数：chars 与行数同为正文规模，行数随换行分布变化）
    if (typeof text !== 'string' || text.length === 0) { return 0; }
    return text.split('\n').length;
}

function thinkStatsText(p) {
    // 统计项文本——` · <字符数> 字符 · <行数> 行 · <用时> · <速率> 字符/s`
    // 缺失路径（durMs ≤ 0——历史重建 / 刷新重建）→ 时间与速率位跳过，尾附「未统计」
    var parts = [];
    var chars = (typeof p.chars === 'number') ? p.chars : 0;
    parts.push(chars + ' 字符');
    parts.push(thinkLineCount(p.text) + ' 行');
    var durMs = (typeof p.durMs === 'number') ? p.durMs : -1;
    if (durMs > 0) {
        parts.push(fmtMs(durMs));
        var cps = (typeof p.cps === 'number') ? p.cps : -1;
        if (cps >= 0) {
            parts.push(cps + ' 字符/s');
        }
        else {
            parts.push('未统计');
        }
    }
    else {
        parts.push('未统计');
    }
    return ' · ' + parts.join(' · ');
}

function thinkPeek(text) {
    // 压缩档内容——<3 行不压缩直接显示原文；≥3 行 = 首行 + 折叠提示 + 末行
    // 折叠提示口径（A199 2026-10-06）——`…已折叠 N行 M字符…`，N/M 为省略段（首末行之间）的规模；
    // 字符数含段内换行符，与载荷 chars 口径对齐
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) { return text; }
    var hiddenLines = lines.length - 2;
    var hiddenChars = text.length - lines[0].length - lines[lines.length - 1].length - 2;
    if (hiddenChars < 0) { hiddenChars = 0; }
    return lines[0] + '\n…已折叠 ' + hiddenLines + '行 ' + hiddenChars + '字符…\n' + lines[lines.length - 1];
}

function buildReasonBlock(payload) {
    // 完成态思考块——头行（标签 + 统计）+ 两档正文（两档内容同置 DOM，显示切换纯 CSS：点击只切类，零内容重建）
    // 气泡外壳（2026-10-05 气泡化）——块体 = 气泡；内层 .chat-think 保留原结构（底色与描边由外壳承担）
    var p = payload || {};
    var content = (typeof p.text === 'string') ? p.text : '';

    var row = blockRow('reason');
    var bubble = el('div', bodyClass('reason'));
    var box = el('div', 'chat-think done');

    var head = el('div', 'ct-head');
    head.appendChild(elText('span', 'ct-label', 'Think'));
    head.appendChild(elText('span', 'ct-stat', thinkStatsText(p)));
    box.appendChild(head);

    var body = el('div', 'ct-body');
    body.appendChild(elText('div', 'ct-peek', thinkPeek(content)));
    body.appendChild(elText('div', 'ct-full', content));
    box.appendChild(body);

    // 切档交互（A199 收窄）——热区只落头行（正文可自由选中复制）；CSS 指针提示同源
    bindPressToggle(head, function () { thinkToggle(box); });
    bubble.appendChild(box);
    row.appendChild(bubble);
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
