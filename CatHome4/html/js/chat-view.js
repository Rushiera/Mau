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

// 思考段终结——折叠全部仍在展开的思考块（2026-09-17：思考结束立即折叠，不再为最终回复保留展开态）
// 调用面：reply 流式开始（chatOnStream kind=text）+ 回复整块到达（chatOnText）——折叠后 summary 走折叠摘要渲染
function chatCollapseReasons() {
    var all = chatMsgs.querySelectorAll('details.chat-reason');
    for (var i = 0; i < all.length; i++) {
        if (all[i].open !== true) { continue; }
        all[i].open = false;
        if (all[i]._updateSummary) { all[i]._updateSummary(); }
    }
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
    if (n === 'powershell7') { return '💠'; }
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

// 结果规模信息——ps 结果 JSON 解析（stdout/stderr 合计 + 截断/超时标记）；非 JSON 按纯文本长度
// 动机（2026-09-14）：消耗可见才能优化——16KB 硬截断保留，前端把规模与截断状态显式呈现
function chatResultInfo(resultText) {
    var text = resultText || '';
    var info = { chars: text.length, truncated: false, timeout: false };
    if (text.length === 0) {
        return info;
    }
    var trimmed = text.replace(/^\s+|\s+$/g, '');
    if (trimmed.charAt(0) !== '{') {
        return info;
    }
    try {
        var o = JSON.parse(trimmed);
        if (o && typeof o.stdout === 'string') {
            var n = o.stdout.length;
            if (typeof o.stderr === 'string') { n = n + o.stderr.length; }
            info.chars = n;
            info.truncated = (o.truncated === true);
            info.timeout = (o.timeout === true);
        }
    } catch (e) {
        // 非 JSON——按纯文本长度（info 已就绪）
    }
    return info;
}

// 折叠行消耗标注——大结果（≥1000 字符）或截断/超时才追加（小结果不标，避免噪音）
function chatResultSuffix(info) {
    if (!info) {
        return '';
    }
    if (info.truncated) {
        return ' · ⚠️ 已达上限 ' + chatFmtCount(info.chars) + ' 字符' + (info.timeout ? '（超时终止）' : '');
    }
    if (info.chars >= 1000) {
        return ' · ' + chatFmtCount(info.chars) + ' 字符';
    }
    return '';
}

// 展开态整块点击收起（2026-09-18）——原生 details 只有折叠头（summary）可点，展开后内容区长；
// 工具卡 / 思考块展开时整块任意位置点击即收起（折叠头仍走原生 toggle）
// 收起判据（四道关）：折叠头本身 · 交互元素（按钮/链接/输入）· 按下时已有选区 · 按下→抬起位移 <20px 且时长 <0.3s
function chatHasSelection() {
    if (typeof window.getSelection !== 'function') { return false; }
    var sel = window.getSelection();
    if (!sel || sel.isCollapsed === true) { return false; }
    return sel.toString().length > 0;
}

// 死区阈值——按下→抬起的位移/时长上限；超限视为拖选或长按，不触发收起
var CHAT_COLLAPSE_MAX_MOVE = 20;
var CHAT_COLLAPSE_MAX_MS = 300;

function chatBindBodyCollapse(det) {
    det.addEventListener('mousedown', function (e) {
        det._press = {
            x: e.clientX || 0,
            y: e.clientY || 0,
            t: Date.now(),
            sel: chatHasSelection()
        };
    });
    det.addEventListener('click', function (e) {
        if (det.open !== true) { return; }
        var t = e.target;
        if (t && typeof t.closest === 'function') {
            // 内层段（.seg）让位——段折叠头与段内容都不收起整块（2026-09-18 骨架层）
            if (t.closest('.seg')) { return; }
            if (t.closest('summary') || t.closest('button, a, input, textarea, select')) { return; }
        }
        var p = det._press;
        if (p) {
            det._press = null;
            if (p.sel === true) { return; }
            if (Math.abs((e.clientX || 0) - p.x) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if (Math.abs((e.clientY || 0) - p.y) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if ((Date.now() - p.t) > CHAT_COLLAPSE_MAX_MS) { return; }
        }
        det.open = false;
        if (det._updateSummary) { det._updateSummary(); }
    });
}

function chatToolCard(tool, open) {
    // 工具卡——details 结构（open=true 展开：两段式先行卡直接展示 ⏳；缺省折叠——点击 summary 展开/收起）
    // .tn/.ta/.tr 类保留——测试与样式复用；result === undefined → 「⏳ 处理中…」占位（完成时整卡替换）
    var isErr = tool.result && tool.result.indexOf('ERR') === 0;
    var det = document.createElement('details');
    det.className = 'chat-tool' + (isErr ? ' err' : '');
    det.open = (open === true);
    var sum = document.createElement('summary');
    sum.className = 'tn';
    // 工具前缀——对齐 CH2：并发批次显示 [icon n/m]；单次保持现状（🔧/⚠️）
    var prefix;
    if (isErr) {
        prefix = (tool.toolTotal > 1) ? ('[⚠️ ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ') : '⚠️ ';
    } else if (tool.toolTotal > 1) {
        prefix = '[' + chatToolIcon(tool.name) + ' ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ';
    } else {
        // 单发同样用类型图标（原固定 🔧 扳手——2026-09-17）
        prefix = chatToolIcon(tool.name) + ' ';
    }
    // 可见性适配——powershell 命令硬解码为自然语言意图（chat-cmd.js；宿主 summary 为原始命令截断，此处覆盖）
    var isPs = (tool.name === 'powershell' || tool.name === 'powershell7');
    var summaryText = tool.summary || tool.name || '?';
    var cmdIntent = null;
    if (isPs && typeof cmdDecodeTool === 'function') {
        cmdIntent = cmdDecodeTool(tool.arguments);
        if (cmdIntent) { summaryText = cmdIntent.brief; }
    }
    var resultInfo = chatResultInfo(tool.result);
    // 骨架分派（chat-tools.js）——命中 → 段结构（含工具变体标签）；未登记 → null 走回落路径
    var body = (typeof chatToolBody === 'function') ? chatToolBody(tool) : null;
    sum.textContent = '';
    sum.appendChild(document.createTextNode(prefix));
    if (body && body.tag) {
        // 工具变体标签——双线工具的显式区分（如 PowerShell PS 5.1 / PS 7）
        var tagEl = document.createElement('span');
        tagEl.className = 'ps-tag ' + (body.tagCls || '');
        tagEl.textContent = body.tag;
        sum.appendChild(tagEl);
        sum.appendChild(document.createTextNode(' '));
    }
    sum.appendChild(document.createTextNode(summaryText + chatResultSuffix(resultInfo)));
    det.appendChild(sum);
    if (cmdIntent) {
        // 展开区首块——逐段意图对照（原文仍在下方 arguments 块；未识别段标 ❓）
        var ci = document.createElement('div');
        ci.className = 'cmd-intent';
        ci.textContent = cmdIntent.detail;
        det.appendChild(ci);
    }
    if (body && body.segs) {
        // 骨架路径——输入 / 输出段（二级折叠；段内沿用 .ta / .tr / .ta.warn 锚点类）
        for (var si = 0; si < body.segs.length; si++) {
            det.appendChild(body.segs[si]);
        }
    } else {
        // 回落路径——未登记工具保持现行渲染（零回归）
        if (tool.arguments) {
            var a = document.createElement('div');
            a.className = 'ta';
            a.textContent = tool.arguments;
            det.appendChild(a);
        }
        if (resultInfo.truncated) {
            // 截断警示——结果未完整回传（16KB 上限），消耗信号显式化
            var w = document.createElement('div');
            w.className = 'ta warn';
            w.textContent = '⚠️ 输出已达上限被截断——后续内容未回传' + (resultInfo.timeout ? '；进程超时已终止' : '');
            det.appendChild(w);
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
    }
    chatBindBodyCollapse(det);
    return det;
}

function chatReasonBlock(text, open) {
    // 思考块——默认折叠（点击 summary 展开/收起）；open=true 流式展开（增量可见）
    // 折叠摘要：首行 +（N行M字符已省略显示）+ 末行——最小化显示过程；展开态回退字数标题
    // 渲染入口 det._updateSummary——toggle 事件（真实浏览器）+ 显式调用（流式/整块替换/jsdom 测试）双驱动，幂等同结果
    var det = document.createElement('details');
    det.className = 'chat-reason';
    det.open = (open === true);
    det._reasonText = text || '';
    var sum = document.createElement('summary');
    det.appendChild(sum);
    var pre = document.createElement('div');
    pre.textContent = text || '';
    det.appendChild(pre);
    det._updateSummary = function () {
        var t = det._reasonText || '';
        if (det.open) {
            sum.textContent = (t.length > 0) ? ('Thinking · ' + t.length) : 'Think';
            return;
        }
        // 折叠——Think 标签 + 灰色缩略内容（颜色分工：标签走 Think 态色，内容取展开正文原灰）
        sum.textContent = '';
        var label = document.createElement('span');
        label.className = 'rs-label';
        label.textContent = 'Think';
        sum.appendChild(label);
        var peek = chatReasonFoldedPeek(t);
        if (peek.length > 0) {
            var body = document.createElement('span');
            body.className = 'rs-peek';
            body.textContent = '：' + peek;
            sum.appendChild(body);
        }
    };
    det.addEventListener('toggle', det._updateSummary);
    det._updateSummary();
    chatBindBodyCollapse(det);
    return det;
}

// 折叠摘要内容——<3 行不压缩直接显示原文；≥3 行取 首行 +（N行M字符已省略显示）+ 末行
// （标签 Think 由 summary 渲染侧拼接——本函数只产缩略内容，2026-09-16）
function chatReasonFoldedPeek(text) {
    if (!text || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length < 3) {
        return text;
    }
    var first = lines[0];
    var last = lines[lines.length - 1];
    var midLines = lines.length - 2;
    var midText = lines.slice(1, lines.length - 1).join('\n');
    return first + '（' + midLines + '行' + midText.length + '字符已省略显示）' + last;
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
    // 思考块一律折叠——与实时渲染一致（2026-09-17：思考结束即折叠，无最终回复展开特例）
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
            // S2 §8.4——历史重建：重试过程记录气泡（retry 块随 view.json 落盘；A55 改走渲染单例）
            chatRenderRetry(p);
        } else if (blk.renderType === 'error') {
            // A55——历史重建：LLM 错误气泡（error 块随 view.json 落盘；与实时事件共用渲染单例）
            chatRenderError(p.text || 'LLM 错误');
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
    // P20-P3-7 归属修正——渲染层不写全局状态：sessionId 随返回值交调用方（chat-core.chatLoadHistory）落库
    var sid = data.sessionId || CHAT_SESSION;
    var infoText = '会话 ' + chatFmtCount(data.count || 0) + ' 条 | sessionId=' + sid;
    var hs = data.stats;
    if (hs) {
        // 前文长度 = 最近一次请求的单次 prompt（context 字段）；旧数据无 context 时回退累计值
        var ctx = (hs.context !== undefined && hs.context > 0) ? hs.context : (hs.prompt || 0);
        infoText += ' | 前文 ' + chatFmtCount(ctx) + ' tokens';
    }
    chatInfo.textContent = infoText;
    chatScrollBottom(true);
    return data.sessionId || '';
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
        }).catch(function (e) { uiWarn('回滚指令投递', e); });
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
        }).catch(function (e) { uiWarn('分支指令投递', e); });
        chatInfo.textContent = '分支指令已投递——请回到主控界面选择新会话';
    });
    bar.appendChild(rb);
    bar.appendChild(fb);
    bubble.appendChild(bar);
}

// 时长格式化——毫秒 → 可读（<60s → x.xs；≥60s → xm x.xs；≥3600s → xh xm x.xs——hour 最高单位不再进位；状态条/roundsum 共用）
// 2026-09-17：单位英文化（与状态条英文标签同语言——原「x分x.x秒 / x小时x分x.x秒」退役）
function chatFmtMs(ms) {
    var s = (ms || 0) / 1000;
    if (s >= 3600) {
        var hours = Math.floor(s / 3600);
        var rest = s - hours * 3600;
        var mins = Math.floor(rest / 60);
        var secs = rest - mins * 60;
        return hours + 'h' + mins + 'm' + secs.toFixed(1) + 's';
    }
    if (s >= 60) {
        var mins2 = Math.floor(s / 60);
        var secs2 = s - mins2 * 60;
        return mins2 + 'm' + secs2.toFixed(1) + 's';
    }
    return s.toFixed(1) + 's';
}

// roundsum 轮末统计气泡——本轮 Token 消耗 + 工具次数 + 请求次数 + 六态用时 + 总耗时（宿主 CloseRound 推送/历史重建渲染；弱化系统样式）
function chatOnRoundSum(payload) {
    var d = payload.data || {};
    var phases = d.phases || {};
    var miss = (d.miss !== undefined) ? d.miss : ((d.prompt || 0) - (d.cacheHit || 0));
    if (miss < 0) { miss = 0; }
    var b = chatBubble('assistant', 'roundsum');
    var html = '<div class="rs-head">📊 Round</div>';
    // cache 命中率——cacheHit / prompt（prompt=0 时 0%）
    var promptTotal = d.prompt || 0;
    var hitRate = (promptTotal > 0) ? ((d.cacheHit || 0) / promptTotal * 100) : 0;
    html += '<div class="rs-tok">↑' + chatFmtCount(promptTotal)
        + ' ↓' + chatFmtCount(d.completion || 0)
        + ' cache ' + chatFmtCount(d.cacheHit || 0)
        + ' miss ' + chatFmtCount(miss)
        + ' 🎯' + hitRate.toFixed(1) + '%</div>';
    // 第二行——工具次数 · 请求次数 · All 总耗时（2026-09-17 重排：All 自六态行挪入本行并列；三段各自着色）
    var toolsLine = '';
    if (d.toolCount > 0) { toolsLine = '<span class="rs-tool">🔧 Tool ' + d.toolCount + '</span>'; }
    if (d.requests !== undefined) {
        toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-api">🔄 Api ' + (d.requests || 0) + '</span>';
    }
    toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-all">⏱ All ' + chatFmtMs(d.elapsedMs) + '</span>';
    html += '<div class="rs-tools">' + toolsLine + '</div>';
    // 六态用时（idle 不计时故不入载荷；非零态上尾巴 + All 总计）
    var phasesMeta = [
        { key: 'link', label: 'Link' },
        { key: 'wait', label: 'Wait' },
        { key: 'think', label: 'Think' },
        { key: 'tool', label: 'Tool' },
        { key: 'run', label: 'Run' },
        { key: 'reply', label: 'Reply' }
    ];
    // 第三行——六态用时（All 已挪至第二行；单色弱化——2026-09-17 试过分色，观感偏杂，回退全灰）
    var tparts = [];
    for (var pi = 0; pi < phasesMeta.length; pi++) {
        var pv = phases[phasesMeta[pi].key] || 0;
        if (pv > 0) { tparts.push(phasesMeta[pi].label + ' ' + chatFmtMs(pv)); }
    }
    html += '<div class="rs-times">' + tparts.join(' · ') + '</div>';
    b.innerHTML = html;
}
