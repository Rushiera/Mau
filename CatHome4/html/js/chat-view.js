// CH4 外观层——chat-view.js：对话渲染面（气泡/工具卡/思考块/注入报告/历史渲染/roundsum）——chat-core.js 拆分（2026-09-08 体量治理）
// 加载顺序：chat-md.js → chat-view.js → chat-core.js → chat-note.js（chat.html 引导层引用）
// 职责：纯渲染——气泡 DOM 构造 + 视图块 HTML；无状态无网络；状态/分发/指令在 chat-core.js
// 单向数据流铁律：本文件只被 chat-core.js 的事件分发调用，不自行产生消息

// 计数格式化——大数转 k/M（≥1000 → x.xx k；≥1000000 → x.xx M；M 为最大单位；保留两位小数；sessionId 等标识不适用）
function chatFmtCount(n) {
    n = Number(n) || 0;
    if (n >= 1000000) { return (n / 1000000).toFixed(2) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(2) + 'k'; }
    return String(n);
}

function chatBubble(role, cls) {
    var row = document.createElement('div');
    row.className = 'chat-row ' + role;
    var b = document.createElement('div');
    b.className = 'chat-bubble' + (cls ? ' ' + cls : '');
    row.appendChild(b);
    chatMsgs.appendChild(row);
    chatScrollBottom();
    return b;
}

function chatAppend(bubble, text) {
    // 专用文本节点追加——textContent 拼接会销毁子元素（details/工具卡被后续 text 事件清空——B4.2 根因）
    if (!bubble.textNode) {
        bubble.textNode = document.createTextNode('');
        bubble.insertBefore(bubble.textNode, bubble.firstChild);
    }
    bubble.textNode.data = bubble.textNode.data + text;
    chatScrollBottom();
}

// 工具图标映射——CH2 CH_Tool_LLMToolDisplay.GetIcon 移植（CH4 连字符工具名适配）
function chatToolIcon(name) {
    var n = name || '';
    if (n === 'text-read' || n === 'text-read_between' || n === 'text-read_lines') { return '📖'; }
    if (n === 'text-write') { return '✏️'; }
    if (n === 'text-append') { return '📎'; }
    if (n === 'text-replace') { return '🔄'; }
    if (n === 'text-find' || n === 'text-grep') { return '🔍'; }
    if (n === 'text-tree') { return '🌲'; }
    if (n === 'text-move') { return '📦'; }
    if (n === 'text-delete') { return '🗑️'; }
    if (n.indexOf('cs-') === 0) { return '🐎'; }
    if (n.indexOf('config-') === 0) { return '⚙️'; }
    if (n.indexOf('mau-') === 0) { return '🧱'; }
    if (n === 'powershell') { return '💻'; }
    if (n === 'web-search') { return '🌐'; }
    if (n === 'image-analyze') { return '🖼️'; }
    if (n === 'temp-info' || n === 'temp-exec') { return '🧪'; }
    if (n === 'Note') { return '📋'; }
    if (n === 'time') { return '🕐'; }
    if (n === 'random') { return '🎲'; }
    if (n === 'info') { return 'ℹ️'; }
    if (n.indexOf('host-') === 0) { return '⚡'; }
    return '🔹';
}

function chatToolCard(tool) {
    // 工具卡——details 结构默认折叠（点击 summary 展开/收起；.tn/.ta/.tr 类保留——测试与样式复用）
    var isErr = tool.result && tool.result.indexOf('ERR') === 0;
    var det = document.createElement('details');
    det.className = 'chat-tool' + (isErr ? ' err' : '');
    det.open = false;
    var sum = document.createElement('summary');
    sum.className = 'tn';
    // 工具前缀——对齐 CH2：并发批次显示 [icon n/m]；单次保持现状（🔧/⚠️）
    var prefix;
    if (isErr) {
        prefix = (tool.toolTotal > 1) ? ('[⚠️ ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ') : '⚠️ ';
    } else if (tool.toolTotal > 1) {
        prefix = '[' + chatToolIcon(tool.name) + ' ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ';
    } else {
        prefix = '🔧 ';
    }
    sum.textContent = prefix + (tool.summary || tool.name || '?');
    det.appendChild(sum);
    if (tool.arguments) {
        var a = document.createElement('div');
        a.className = 'ta';
        a.textContent = tool.arguments;
        det.appendChild(a);
    }
    if (tool.result) {
        var r = document.createElement('div');
        r.className = 'tr' + (isErr ? ' err' : '');
        r.textContent = tool.result;
        det.appendChild(r);
    } else if (tool.result === undefined) {
        var w = document.createElement('div');
        w.className = 'ta';
        w.textContent = '⏳ 处理中…';
        det.appendChild(w);
    }
    return det;
}

function chatReasonBlock(text, open) {
    // 思考块——默认折叠（点击 summary 展开/收起；折叠时 summary 显示字数感知内容量）；open=true 流式展开（增量可见）
    var det = document.createElement('details');
    det.className = 'chat-reason';
    det.open = (open === true);
    var sum = document.createElement('summary');
    sum.textContent = (text && text.length > 0) ? ('思考过程 · ' + text.length + ' 字') : '思考过程';
    det.appendChild(sum);
    var pre = document.createElement('div');
    pre.textContent = text || '';
    det.appendChild(pre);
    return det;
}

// 注入报告 HTML——新会话前文加载明细（ok/missing/error 三态 + 字符数 + 注入工具组；history 首块渲染）
function chatInjectReportHtml(p) {
    var files = p.files || [];
    var total = p.total || 0;
    var ok = p.ok || 0;
    var missing = p.missing || 0;
    var failed = p.failed || 0;
    var html = '<div class="inject-report">'
        + '<div class="ir-head">📚 前文加载：' + ok + '/' + total + ' 成功'
        + (missing > 0 ? ' · 缺失 ' + missing : '')
        + (failed > 0 ? ' · 失败 ' + failed : '')
        + '</div>';
    if (files.length > 0) {
        html += '<div class="ir-list">';
        for (var i = 0; i < files.length; i++) {
            var f = files[i];
            var icon = '✅';
            var cls = 'ok';
            if (f.status === 'missing') { icon = '⚠️'; cls = 'missing'; }
            else if (f.status === 'error') { icon = '❌'; cls = 'error'; }
            html += '<div class="ir-item ' + cls + '">' + icon + ' ' + escapeHtml(f.file || '')
                + (f.status === 'ok' && f.chars > 0 ? '（' + f.chars + ' 字符）' : '')
                + (f.message ? ' — ' + escapeHtml(f.message) : '')
                + '</div>';
        }
        html += '</div>';
    }
    // Q5 注入工具组——独立气泡（组名 + 工具名列表；提示"新会话已加载工具"；desc 不进展示）
    var groups = p.toolGroups || [];
    if (groups.length > 0) {
        var toolCount = 0;
        for (var g = 0; g < groups.length; g++) { toolCount = toolCount + (groups[g].tools || []).length; }
        html += '<div class="ir-tools"><div class="ir-tools-head">🛠️ 已启用 ' + toolCount + ' 个工具</div>';
        for (var g2 = 0; g2 < groups.length; g2++) {
            var names = [];
            var tools = groups[g2].tools || [];
            for (var t = 0; t < tools.length; t++) { names.push(tools[t].name); }
            html += '<div class="ir-tool-row"><span class="ir-tool-group">' + escapeHtml(groups[g2].group || '内置') + '</span> —— ' + escapeHtml(names.join('、')) + '</div>';
        }
        html += '</div>';
    }
    html += '</div>';
    return html;
}

function chatRenderHistory(data) {
    // F4 视图块历史渲染——按 blocks[] renderType 分派（view 协议；无 messages[] 旧结构）
    chatMsgs.textContent = '';
    var blocks = data.blocks || [];
    for (var i = 0; i < blocks.length; i++) {
        var blk = blocks[i];
        var p = blk.payload || {};
        if (blk.renderType === 'user') {
            var ub = chatBubble('user');
            ub.textContent = p.content || '';
        } else if (blk.renderType === 'reason') {
            var rb = chatBubble('assistant', 'reason');
            rb.appendChild(chatReasonBlock(p.content || ''));
        } else if (blk.renderType === 'toolcard') {
            var tb = chatBubble('assistant', 'tool');
            tb.appendChild(chatToolCard(p));
        } else if (blk.renderType === 'retry') {
            // S2 §8.4——历史重建：重试过程记录气泡（retry 块随 view.json 落盘）
            var st = p.state || 'retrying';
            var txt;
            if (st === 'resolved') {
                txt = '✓ 已恢复' + (p.attempt ? '（重试 ' + p.attempt + ' 次）' : '');
            } else {
                txt = '⟳ 重试中 ' + (p.attempt || '') + '/' + (p.max || '') + (p.text ? ' · ' + p.text : '');
            }
            var rtb = chatBubble('assistant', 'retry');
            rtb.textContent = txt;
            if (st === 'resolved') { rtb.classList.add('resolved'); }
        } else if (blk.renderType === 'inject_report') {
            // 注入报告——新会话前文加载明细（ok/missing/error 三态 + 字符数；独立持久化字段 Rebuild 不清）
            var rb2 = chatBubble('assistant', 'inject');
            rb2.innerHTML = chatInjectReportHtml(p);
        } else if (blk.renderType === 'roundsum') {
            // roundsum 轮末统计——历史重建：独立气泡（本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时）
            chatOnRoundSum(p);
        } else if (blk.renderType === 'text') {
            // F3 MD 渲染——历史 text 块同样走解析器（与实时渲染一致）；md-block 包裹=CSS 作用域锚点
            // P6b 节点操作条——msgIndex 顶层字段（视图块携带真实前文顺序；roundsum/inject_report=-1 不挂）
            var cb = chatBubble('assistant');
            cb.innerHTML = '<div class="md-block">' + mdToHtml(p.content || '') + '</div>';
            chatAppendNodeActions(cb, blk.msgIndex);
        }
    }
    // P9.3 会话归属动态化——SSE sessionId 随会话 ID（时间戳）变化；history 先于任何 view 事件到达（loading→idle 时序保证）
    if (data.sessionId) {
        CHAT_SESSION = data.sessionId;
    }
    var infoText = '会话 ' + chatFmtCount(data.count || 0) + ' 条 | sessionId=' + CHAT_SESSION;
    var hs = data.stats;
    if (hs) {
        // 前文长度 = 最近一次请求的单次 prompt（context 字段）；旧数据无 context 时回退累计值
        var ctx = (hs.context !== undefined && hs.context > 0) ? hs.context : (hs.prompt || 0);
        infoText += ' | 前文 ' + chatFmtCount(ctx) + ' tokens';
    }
    chatInfo.textContent = infoText;
    chatScrollBottom(true);
}

// P6b 节点操作条——text 块底部两按钮（⟲ 回滚 / ⧉ 分支）；指令走 command 总线（单向数据流：前端零寻路，只回传 MsgIndex）
function chatAppendNodeActions(bubble, msgIndex) {
    if (msgIndex === undefined || msgIndex === null || msgIndex < 0) { return; }
    var bar = document.createElement('div');
    bar.className = 'node-actions';
    var rb = document.createElement('button');
    rb.type = 'button';
    rb.className = 'node-btn node-btn-rollback';
    rb.title = '从此处继续对话（回滚——该回复后的内容将截断，不可恢复）';
    rb.textContent = '⟲';
    rb.addEventListener('click', function () {
        if (!window.confirm('从此处继续对话？该回复之后的所有消息将被截断（不可恢复）。')) { return; }
        fetch('/api/v1/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: 'session.rollback ' + msgIndex })
        }).catch(function () {});
        chatInfo.textContent = '回滚已投递——建议刷新浏览器页面';
    });
    var fb = document.createElement('button');
    fb.type = 'button';
    fb.className = 'node-btn node-btn-fork';
    fb.title = '从此处新建独立 Cat（以该回复为起点分支新实例，继承配置与前文）';
    fb.textContent = '⧉';
    fb.addEventListener('click', function () {
        var name = window.prompt('新 Cat 显示名：', 'fork-' + msgIndex);
        if (!name || name.length === 0) { return; }
        fetch('/api/v1/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: 'session.fork ' + name + ' ' + msgIndex })
        }).catch(function () {});
        chatInfo.textContent = '分支指令已投递——请回到主控界面选择新会话';
    });
    bar.appendChild(rb);
    bar.appendChild(fb);
    bubble.appendChild(bar);
}

// 时长格式化——毫秒 → 可读（<60s → x.xs；≥60s → x分x.x秒；roundsum 用时展示）
function chatFmtMs(ms) {
    var s = (ms || 0) / 1000;
    if (s >= 60) {
        var mins = Math.floor(s / 60);
        var secs = s - mins * 60;
        return mins + '分' + secs.toFixed(1) + '秒';
    }
    return s.toFixed(1) + 's';
}

// roundsum 轮末统计气泡——本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时（宿主 CloseRound 推送/历史重建渲染；弱化系统样式）
function chatOnRoundSum(payload) {
    var d = payload.data || {};
    var phases = d.phases || {};
    var miss = (d.miss !== undefined) ? d.miss : ((d.prompt || 0) - (d.cacheHit || 0));
    if (miss < 0) { miss = 0; }
    var b = chatBubble('assistant', 'roundsum');
    var html = '<div class="rs-head">📊 本轮统计</div>';
    // cache 命中率——cacheHit / prompt（prompt=0 时 0%）
    var promptTotal = d.prompt || 0;
    var hitRate = (promptTotal > 0) ? ((d.cacheHit || 0) / promptTotal * 100) : 0;
    html += '<div class="rs-tok">↑' + chatFmtCount(promptTotal)
        + ' ↓' + chatFmtCount(d.completion || 0)
        + ' cache ' + chatFmtCount(d.cacheHit || 0)
        + ' miss ' + chatFmtCount(miss)
        + ' 🎯' + hitRate.toFixed(1) + '%</div>';
    if (d.toolCount > 0) {
        html += '<div class="rs-tools">🔧 工具 ' + d.toolCount + ' 次</div>';
    }
    html += '<div class="rs-times">⏱ 链路 ' + chatFmtMs(phases.link)
        + ' · 思考 ' + chatFmtMs(phases.think)
        + ' · 工具 ' + chatFmtMs(phases.tool)
        + ' · 回复 ' + chatFmtMs(phases.reply)
        + ' · 总计 ' + chatFmtMs(d.elapsedMs) + '</div>';
    b.innerHTML = html;
}
