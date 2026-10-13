// ═══════════════════════════════════════════
// blocks/systemautomessage.js —— 系统注入块（persist 类 · 独立渲染）
//
// 契约：
//   item = { type:'systemautomessage', ts, msgIndex, round, payload:{ text, source?, origin? } }
//   source = 来源族名（systemauto / sleep / timer / delay / restart）
//   text   = 落前文文本（含固定行头前缀 [systemauto]）
//   渲染顺序即到达顺序——本层不排序、不去重、不算键
//
// 产出：.chat-row.sysmsg > .chat-bubble > .chat-sys.done
//         ├ .sy-head（System 标签 + 统计项——切档唯一热区）
//         └ .sy-body ├ .sy-peek 压缩档（默认显示）
//                    └ .sy-full 展开档（点击头行切 .full 类，纯 CSS 换显）
//
// 结构照搬 Think 块（压缩档 = 前 1 行 + 折叠提示 + 末 1 行；展开档限高滚动），
//   🔴 独立实现——不复用 `buildReasonBlock` / `thinkToggle` / `.chat-think` 类（同名同形不同源）。
// 题头：`System · <字符数> 字符 · <行数> 行 · <来源族名>`（字符数 / 行数为显示派生——外观格式化，零业务效果）。
//
// 来源：后端 ChatSession.AppendInjection（sys 路径）→ SessionViewStore.OnSystemMessage
// ═══════════════════════════════════════════

function sysLineCount(text) {
    // 行数——显示派生（换行符计数）
    if (typeof text !== 'string' || text.length === 0) { return 0; }
    return text.split('\n').length;
}

function sysStatsText(p) {
    // 统计项——` · <字符数> 字符 · <行数> 行 · <来源族名>`
    var text = (typeof p.text === 'string') ? p.text : '';
    var parts = [];
    parts.push(text.length + ' 字符');
    parts.push(sysLineCount(text) + ' 行');
    parts.push((typeof p.source === 'string' && p.source.length > 0) ? p.source : 'systemauto');
    return ' · ' + parts.join(' · ');
}

function sysPeek(text) {
    // 压缩档——<3 行直显；≥3 行 = 首行 + 折叠提示 + 末行（口径同 Think：折叠行/字符为省略段规模）
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) { return text; }
    var hiddenLines = lines.length - 2;
    var hiddenChars = text.length - lines[0].length - lines[lines.length - 1].length - 2;
    if (hiddenChars < 0) { hiddenChars = 0; }
    return lines[0] + '\n…已折叠 ' + hiddenLines + '行 ' + hiddenChars + '字符…\n' + lines[lines.length - 1];
}

function sysToggle(box) {
    // 高度档切换——压缩（默认）⇄ 展开（限高滚动）；类切换（非 details 语义）
    if (!box) { return; }
    if (box.classList.contains('full')) {
        box.classList.remove('full');
    } else {
        box.classList.add('full');
    }
}

function buildSystemAutoMessageBlock(payload) {
    // 系统注入块——头行（System + 统计）+ 两档正文（两档同置 DOM，显示切换纯 CSS：点击只切类，零内容重建）
    var p = payload || {};
    var content = (typeof p.text === 'string') ? p.text : '';

    var row = blockRow('systemautomessage');
    var bubble = el('div', bodyClass('systemautomessage'));
    var box = el('div', 'chat-sys done');

    var head = el('div', 'sy-head');
    head.appendChild(elText('span', 'sy-label', 'System'));
    head.appendChild(elText('span', 'sy-stat', sysStatsText(p)));
    box.appendChild(head);

    var body = el('div', 'sy-body');
    body.appendChild(elText('div', 'sy-peek', sysPeek(content)));
    body.appendChild(elText('div', 'sy-full', content));
    box.appendChild(body);

    // 切档交互——热区只落头行（正文可自由选中复制）
    bindPressToggle(head, function () { sysToggle(box); });
    bubble.appendChild(box);
    row.appendChild(bubble);
    return row;
}
