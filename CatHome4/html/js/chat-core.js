// CH4 外观层——chat-core.js：对话核心（view 协议渲染 + 四态状态条 + 发送 + 历史）——F2.3 自 chat.html 内联拆出（模块化；对话逻辑唯一真相源）
// 加载顺序：chat-core.js → chat-note.js（chat.html 引导层引用）；不依赖 app.js（自包含 DOM 引用——独立对话页无 app.js）
// 单向数据流铁律：SSE view/note 事件唯一渲染入口；发送走 command 总线；user 事件唯一气泡来源；前端零业务逻辑
// SSE/按钮绑定/初始化由 chat.html 引导层承接（applyUiConfig + EventSource view/note + 防御式 addEventListener + chatLoadHistory）

// [段1] 对话区状态（B4 同构——自 index.html 提取；P9.3d 独立对话页；F4 迁 view 协议）
function escapeHtml(s) {
    // 文本安全转义——聊天内容/Note 任务渲染共用（XSS 与格式双防）
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
var chatMsgs = document.getElementById('chatMsgs');
var chatInfo = document.getElementById('chatInfo');
var chatInput = document.getElementById('chatSendInput');
var chatBtn = document.getElementById('chatSendBtn');
var chatState = 'loading';        // loading/idle/sending
var viewContainers = {};          // F4 视图容器——seq → {type:'text'|'reason', bubble, reasonPre}（流式容器；整块替换后删除）
var chatTimer = null;             // 120s 无响应兜底定时器
var CHAT_SESSION = '';
var CHAT_TIMEOUT_MS = 120000;
var chatPending = [];              // 插话队列——本地发送记录（user 事件到达 FIFO 移除；纯展示）
var SYSTEM_AUTO_PREFIX = '[SystemAuto] ';   // 系统自动消息前缀（前端常量一处定义，微调只动此行）
var chatPendingReset = false;      // 会话重置待确认——chatNewSession 置位；session_reset/chatdone 消费（miss 兜底）

// E 系列——四态状态条（link 链路/think 思考/tool 工具/reply 回复）+ 每态计时 + Token 统计（CH2 对话流形态移植）
var chatPhases = [
    { key: 'link', label: '链路', icon: '🔗' },
    { key: 'think', label: '思考', icon: '🧠' },
    { key: 'tool', label: '工具', icon: '🔧' },
    { key: 'reply', label: '回复', icon: '💬' }
];
var chatActivePhase = null;        // 当前态 key（null=无活跃轮）
var chatPhaseStart = 0;            // 当前态开始时间戳（ms）
var chatPhaseTimes = { link: 0, think: 0, tool: 0, reply: 0 };   // 各态已完成秒数（累计）
var chatUsage = { prompt: 0, completion: 0, cacheHit: 0 };       // Token 整轮累计（usage 事件覆盖式累计）
var chatStatusTimer = null;        // 状态条 500ms 刷新定时器

// 计数格式化——大数转 k/M（≥1000 → x.xx k；≥1000000 → x.xx M；M 为最大单位；保留两位小数；sessionId 等标识不适用）
function chatFmtCount(n) {
    n = Number(n) || 0;
    if (n >= 1000000) { return (n / 1000000).toFixed(2) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(2) + 'k'; }
    return String(n);
}

function chatPhaseEnter(key) {
    // 相位切换——前一态结算 + 新态起表（事件驱动；每态计时独立）
    var now = Date.now();
    if (chatActivePhase && chatActivePhase !== key && chatPhaseStart > 0) {
        chatPhaseTimes[chatActivePhase] = chatPhaseTimes[chatActivePhase] + (now - chatPhaseStart) / 1000;
    }
    if (chatActivePhase !== key) {
        chatActivePhase = key;
        chatPhaseStart = now;
    }
    chatRenderStatus();
}

function chatPhaseReset() {
    // 终态/失败——状态条全部清零隐藏（保留 usage 累计——chatdone 后 token 统计需持续可见）
    chatActivePhase = null;
    chatPhaseStart = 0;
    chatPhaseTimes = { link: 0, think: 0, tool: 0, reply: 0 };
    if (chatStatusTimer) { clearInterval(chatStatusTimer); chatStatusTimer = null; }
    chatRenderStatus();
}

function chatPhaseResetFull() {
    // 发送新轮/新会话——usage 也清零（跨轮不复用）
    chatPhaseReset();
    chatUsage = { prompt: 0, completion: 0, cacheHit: 0 };
    chatRenderStatus();
}

function chatRenderStatus() {
    // 状态条渲染——已完成态暗色 + 当前态高亮 + 实时秒数 + 右侧 Token 统计
    var bar = document.getElementById('chatStatus');
    if (!bar) { return; }
    var html = '';
    for (var i = 0; i < chatPhases.length; i++) {
        var p = chatPhases[i];
        var sec = chatPhaseTimes[p.key];
        if (chatActivePhase === p.key && chatPhaseStart > 0) {
            sec = sec + (Date.now() - chatPhaseStart) / 1000;
        }
        if (sec <= 0 && chatActivePhase !== p.key) { continue; }
        var cls = (chatActivePhase === p.key) ? 'active' : '';
        html += '<span class="st ' + p.key + ' ' + cls + '">' + p.icon + ' ' + p.label + ' ' + sec.toFixed(1) + 's</span>';
    }
    if (chatUsage.prompt > 0 || chatUsage.completion > 0) {
        var miss = chatUsage.prompt - chatUsage.cacheHit;
        if (miss < 0) { miss = 0; }
        html += '<span class="tok">'
            + '<span class="tk">↑' + chatFmtCount(chatUsage.prompt) + '</span>'
            + '<span class="tk c">↓' + chatFmtCount(chatUsage.completion) + '</span>'
            + (chatUsage.cacheHit > 0 ? '<span class="tk ch">cache ' + chatFmtCount(chatUsage.cacheHit) + '</span>' : '')
            + (miss > 0 ? '<span class="tk ms">miss ' + chatFmtCount(miss) + '</span>' : '')
            + '</span>';
    }
    bar.innerHTML = html;
    if (chatActivePhase && !chatStatusTimer) {
        chatStatusTimer = setInterval(chatRenderStatus, 500);
    } else if (!chatActivePhase && chatStatusTimer) {
        clearInterval(chatStatusTimer);
        chatStatusTimer = null;
    }
}

function chatSetState(s) {
    chatState = s;
    // 单向数据流改造——忙时可插话（排队等本轮结束插入）；仅 loading 禁用
    chatInput.disabled = (s === 'loading');
    chatBtn.disabled = (s === 'loading');
    var nsb = document.getElementById('noteStartBtn');
    if (nsb) { nsb.disabled = (s !== 'idle'); }
    // P6 中止——停止按钮仅 sending 可用（不常用按钮：流式/工具执行中才可点）
    var psb = document.getElementById('chatPause');
    if (psb) { psb.disabled = (s !== 'sending'); }
}

function chatScrollBottom(force) {
    // 智能滚动——用户上翻读历史时不强制拉底（仅在接近底部阈值内自动跟随）；force=true 强制滚底（历史加载/新会话）
    if (!force) {
        var near = chatMsgs.scrollHeight - chatMsgs.scrollTop - chatMsgs.clientHeight;
        if (near >= 80) {
            return;
        }
    }
    chatMsgs.scrollTop = chatMsgs.scrollHeight;
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

function chatKeepAlive() {
    if (chatTimer) { clearTimeout(chatTimer); }
    chatTimer = setTimeout(function () {
        if (chatState === 'sending') {
            // 120s 无任何 view 事件——seal 全部流式容器 + 错误提示 + 恢复 idle
            for (var k in viewContainers) {
                var c = viewContainers[k];
                if (c && c.bubble) {
                    c.bubble.classList.remove('streaming');
                    c.bubble.classList.remove('streaming-wait');
                    chatAppend(c.bubble, '\n（LLM 无响应超过 120s——检查宿主控制台）');
                    c.bubble.classList.add('error');
                }
            }
            viewContainers = {};
            chatSetState('idle');
        }
    }, CHAT_TIMEOUT_MS);
}

function chatSealTextStreams() {
    // 工具轮/新思考——seal 未终结的文本流式容器（防闪烁标记残留；宿主 text 整块为正常 seal 路径，此处兜底协议缺口）
    for (var k in viewContainers) {
        var c = viewContainers[k];
        if (c && c.type === 'text') {
            c.bubble.classList.remove('streaming');
            c.bubble.classList.remove('streaming-wait');
        }
    }
}

function chatToolCard(tool) {
    // 工具卡——details 结构默认折叠（点击 summary 展开/收起；.tn/.ta/.tr 类保留——测试与样式复用）
    var det = document.createElement('details');
    det.className = 'chat-tool';
    det.open = false;
    var sum = document.createElement('summary');
    sum.className = 'tn';
    sum.textContent = '🔧 ' + (tool.summary || tool.name || '?');
    det.appendChild(sum);
    if (tool.arguments) {
        var a = document.createElement('div');
        a.className = 'ta';
        a.textContent = tool.arguments;
        det.appendChild(a);
    }
    if (tool.result) {
        var r = document.createElement('div');
        r.className = 'tr' + (tool.result.indexOf('ERR') === 0 ? ' err' : '');
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

function chatReasonBlock(text) {
    // 思考块——默认折叠（点击 summary 展开/收起；折叠时 summary 显示字数感知内容量）
    var det = document.createElement('details');
    det.className = 'chat-reason';
    det.open = false;
    var sum = document.createElement('summary');
    sum.textContent = (text && text.length > 0) ? ('思考过程 · ' + text.length + ' 字') : '思考过程';
    det.appendChild(sum);
    var pre = document.createElement('div');
    pre.textContent = text || '';
    det.appendChild(pre);
    return det;
}

// 注入报告 HTML——新会话前文加载明细（ok/missing/error 三态 + 字符数；history 首块渲染）
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

function chatLoadHistory() {
    fetch('/api/v1/history')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            chatRenderHistory(d);
            chatSetState('idle');
        })
        .catch(function () {
            chatInfo.textContent = '历史加载失败——宿主未运行？';
            chatSetState('idle');
        });
}

function chatFail(msg) {
    // E 系列——失败：四态状态条清零隐藏
    chatPhaseReset();
    // 发送失败——seal 全部流式容器 + 独立错误气泡 + 恢复 idle
    for (var k in viewContainers) {
        var c = viewContainers[k];
        if (c && c.bubble) {
            c.bubble.classList.remove('streaming');
            c.bubble.classList.remove('streaming-wait');
        }
    }
    viewContainers = {};
    var eb = chatBubble('assistant');
    eb.textContent = msg;
    eb.classList.add('error');
    if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
    // 发送失败——插话队列显示失去意义（内核未收到）
    chatPending = [];
    chatRenderPending();
    chatSetState('idle');
}

// 单向数据流改造——发送零自产气泡：只投递 command 总线，气泡由 view user 事件渲染
function chatSend() {
    var text = chatInput.value.trim();
    if (text.length === 0 || chatState === 'loading') { return; }
    chatInput.value = '';
    // 插话队列——本地记录（内核 user 事件到达后 FIFO 移除）
    chatPending.push(text);
    chatRenderPending();
    if (chatState === 'idle') {
        // idle 发送——清阶段残留 + 上一轮 usage（新轮零统计起算；sending 态插话不清——不破坏当前流式渲染）
        viewContainers = {};
        chatPhaseResetFull();
        // E 系列——发送即进入链路态（插话不干扰活跃轮计时）
        chatPhaseEnter('link');
    }
    chatSetState('sending');
    chatKeepAlive();
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'Chat ' + text })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok && d.error) {
                chatFail('发送失败: ' + d.error);
            }
        })
        .catch(function (err) { chatFail('请求失败: ' + err); });
}

// user 视图块——内核确认消息进 Ctx 的唯一出口（单向数据流：前端气泡唯一来源）
function chatOnUser(payload) {
    // 插话队列 FIFO 移除——本地记录与内核确认对齐
    if (chatPending.length > 0) { chatPending.shift(); }
    chatRenderPending();
    var content = payload.content || '';
    var source = payload.source || 'user';
    var isSystem = (source === 'system');
    // 系统自动消息——[SystemAuto] 前缀（Note 自动拉起/插话等内核自动消息与用户消息视觉区分）
    var display = isSystem ? (SYSTEM_AUTO_PREFIX + content) : content;
    var bubble = chatBubble('user');
    if (isSystem) { bubble.classList.add('system'); }
    bubble.textContent = display;
}

// 插话队列渲染——纯展示（数据源 = 本地发送记录 + 内核 user 事件确认）
function chatRenderPending() {
    var panel = document.getElementById('chatPendingPanel');
    if (!panel) { return; }
    if (chatPending.length === 0) {
        panel.textContent = '';
        return;
    }
    var html = '';
    for (var i = 0; i < chatPending.length; i++) {
        html += '<div>⏳ ' + escapeHtml(chatPending[i]) + '</div>';
    }
    panel.innerHTML = html;
}

// ============ F4 view 协议渲染 ============
// 七种 renderType：user/stream/text/reason/toolcard/control/retry（S2 §8.4——retry 重试过程记录独立气泡）
// stream 流式增量——seq 复用=同容器追加；新 seq=新建容器（text 与 reason 各自独立容器）
// text/reason 整块——replaceSeq 指向被替换的流式容器序号（流式→整块替换；无容器则新建）
// retry 独立气泡——replaceSeq≥0 更新已有重试气泡（多次重试替换不堆叠）；-1 新建

function chatOnView(d) {
    if (chatState === 'loading') { return; }
    var type = d.renderType;
    var payload = d.payload || {};
    if (type === 'user') {
        chatOnUser(payload);
    } else if (type === 'stream') {
        chatOnStream(d.seq, payload);
    } else if (type === 'text') {
        chatOnText(d.seq, d.replaceSeq, payload);
    } else if (type === 'reason') {
        chatOnReason(d.seq, d.replaceSeq, payload);
    } else if (type === 'toolcard') {
        chatOnToolCard(payload);
    } else if (type === 'retry') {
        // S2 §8.4——重试过程记录（独立气泡，弱化样式；replaceSeq≥0 更新已有气泡，否则新建）
        chatOnRetry(d.seq, d.replaceSeq, payload);
    } else if (type === 'control') {
        chatOnControl(payload);
    } else if (type === 'roundsum') {
        // roundsum 轮末统计——独立气泡（本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时）
        chatOnRoundSum(payload);
    }
    chatScrollBottom();
}

function chatOnStream(seq, payload) {
    chatKeepAlive();
    if (payload.kind === 'reasoning') {
        // 思考流式——独立气泡默认折叠（summary 字数实时感知）；首 reasoning 进入思考态
        if (!viewContainers[seq]) {
            // 新思考容器——上一轮文本流式已终结（多轮工具循环残留兜底）
            chatSealTextStreams();
            var b = chatBubble('assistant', 'reason');
            b.classList.add('streaming');
            var det = chatReasonBlock('');
            b.appendChild(det);
            viewContainers[seq] = { type: 'reason', bubble: b, reasonPre: det.querySelector('div') };
            chatPhaseEnter('think');
        }
        var rc = viewContainers[seq];
        rc.reasonPre.textContent = rc.reasonPre.textContent + (payload.text || '');
        // 默认折叠——summary 实时字数感知（流式增量同步）
        var sumEl = rc.bubble.querySelector('summary');
        if (sumEl) { sumEl.textContent = '思考过程 · ' + rc.reasonPre.textContent.length + ' 字'; }
    } else if (payload.kind === 'text') {
        // 回复流式——独立气泡流式；空增量（tool_calls 前的空 content 块）跳过——不建空气泡
        var t = payload.text || '';
        if (t.length === 0) { return; }
        if (!viewContainers[seq]) {
            var tb = chatBubble('assistant');
            tb.classList.add('streaming');
            viewContainers[seq] = { type: 'text', bubble: tb, reasonPre: null };
            // E 系列——首 text 进入回复态
            chatPhaseEnter('reply');
        }
        var tc = viewContainers[seq];
        tc.bubble.classList.remove('error');
        chatAppend(tc.bubble, t);
    }
}

function chatOnText(seq, replaceSeq, payload) {
    // 回复整块——replaceSeq≥0 且容器存在 → 替换流式容器；否则新建气泡
    // F3 MD 渲染——整块 content 一次渲染（流式阶段 textContent 追加，不渲染不完整字符流）；md-block 包裹=CSS 作用域锚点
    var content = payload.content || '';
    var html = '<div class="md-block">' + mdToHtml(content) + '</div>';
    // P6b 节点操作条——正式回复块底部两按钮（回滚/分支）；msgIndex<0（工具轮 seal 文本）不挂
    var msgIndex = (payload.msgIndex !== undefined) ? payload.msgIndex : -1;
    var c = viewContainers[replaceSeq];
    if (c && c.type === 'text') {
        c.bubble.classList.remove('streaming');
        c.bubble.classList.remove('streaming-wait');
        c.bubble.innerHTML = html;
        chatAppendNodeActions(c.bubble, msgIndex);
        delete viewContainers[replaceSeq];
    } else {
        var b = chatBubble('assistant');
        b.innerHTML = html;
        chatAppendNodeActions(b, msgIndex);
    }
    chatPhaseEnter('reply');
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

function chatOnReason(seq, replaceSeq, payload) {
    // 思考整块——replaceSeq≥0 且容器存在 → 替换思考流式容器；否则新建
    var content = payload.content || '';
    var c = viewContainers[replaceSeq];
    if (c && c.type === 'reason') {
        c.bubble.classList.remove('streaming');
        c.bubble.classList.remove('streaming-wait');
        c.reasonPre.textContent = content;
        // 整块替换——summary 字数同步（流式容器默认折叠）
        var sumEl = c.bubble.querySelector('summary');
        if (sumEl) { sumEl.textContent = '思考过程 · ' + content.length + ' 字'; }
        delete viewContainers[replaceSeq];
    } else if (content.length > 0) {
        var b = chatBubble('assistant', 'reason');
        b.appendChild(chatReasonBlock(content));
    }
    chatPhaseEnter('think');
}

function chatOnToolCard(payload) {
    // 工具卡整块（F4 无占位卡——工具卡以结果整块出现）
    chatKeepAlive();
    // 工具执行开始——上一轮文本流式已终结（宿主 seal 缺失兜底）
    chatSealTextStreams();
    var tb = chatBubble('assistant', 'tool');
    tb.appendChild(chatToolCard(payload));
    chatPhaseEnter('tool');
}

// S2 §8.4——重试过程记录气泡（独立视图条目：⟳ 重试中 / ✓ 已恢复；弱化样式不抢占对话主视觉）
function chatOnRetry(seq, replaceSeq, payload) {
    chatKeepAlive();
    var state = payload.state || 'retrying';
    var attempt = payload.attempt || '';
    var max = payload.max || '';
    var reason = payload.text || '';
    var text;
    if (state === 'resolved') {
        text = '✓ 已恢复' + (attempt ? '（重试 ' + attempt + ' 次）' : '');
    } else {
        text = '⟳ 重试中 ' + attempt + '/' + max + (reason ? ' · ' + reason : '');
    }
    // 更新已有气泡（replaceSeq≥0）——attempt 递增/状态变化替换文本，不堆叠
    var key = (replaceSeq >= 0) ? replaceSeq : seq;
    var c = viewContainers[key];
    if (c && c.type === 'retry') {
        c.bubble.textContent = text;
        c.bubble.classList.toggle('resolved', state === 'resolved');
        return;
    }
    // 新建独立气泡——弱化样式（chat-bubble.retry：灰底小号，过程记录）
    var rb = chatBubble('assistant', 'retry');
    rb.textContent = text;
    if (state === 'resolved') { rb.classList.add('resolved'); }
    viewContainers[key] = { type: 'retry', bubble: rb, reasonPre: null };
}

function chatOnControl(payload) {
    var type = payload.type;
    if (type === 'usage') {
        // E 系列——Token 统计：usage 覆盖式显示整轮累计
        var u = payload.data || {};
        chatUsage.prompt = u.prompt || 0;
        chatUsage.completion = u.completion || 0;
        chatUsage.cacheHit = u.cacheHit || 0;
        chatRenderStatus();
    } else if (type === 'error') {
        // LLM 错误——seal 全部流式容器 + 错误提示 + 恢复 idle
        chatPhaseReset();
        for (var k in viewContainers) {
            var c = viewContainers[k];
            if (c && c.bubble) {
                c.bubble.classList.remove('streaming');
                c.bubble.classList.remove('streaming-wait');
                chatAppend(c.bubble, '\n' + (payload.text || 'LLM 错误'));
                c.bubble.classList.add('error');
            }
        }
        viewContainers = {};
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        chatSetState('idle');
    } else if (type === 'session_reset') {
        // 会话重置——session.new 清前文后显式信号（问题一修复：消除本地抢跑竞态；收到即清空再拉 history）
        chatPendingReset = false;
        viewContainers = {};
        chatPhaseResetFull();
        chatMsgs.textContent = '';
        chatInfo.textContent = '新会话——注入完成，重建中…';
        chatLoadHistory();
    } else if (type === 'chatdone') {
        // 会话终态——seal 全部流式容器 + 未回填兜底已由 toolcard 整块覆盖 + 恢复 idle
        chatPhaseReset();
        for (var k2 in viewContainers) {
            var c2 = viewContainers[k2];
            if (c2 && c2.bubble) {
                c2.bubble.classList.remove('streaming');
                c2.bubble.classList.remove('streaming-wait');
            }
        }
        viewContainers = {};
        if (payload.count !== undefined) {
            var doneText = '会话 ' + chatFmtCount(payload.count) + ' 条 | sessionId=' + CHAT_SESSION;
            var ds = payload.stats;
            if (ds) {
                // 前文长度 = 最近一次请求的单次 prompt（context 字段）；旧数据无 context 时回退累计值
                var ctx2 = (ds.context !== undefined && ds.context > 0) ? ds.context : (ds.prompt || 0);
                doneText += ' | 前文 ' + chatFmtCount(ctx2) + ' tokens';
            }
            chatInfo.textContent = doneText;
        }
        if (chatPendingReset) {
            // session_reset 事件 miss 兜底——chatdone 到达仍未确认重置 → 重拉 history 保一致性
            chatPendingReset = false;
            chatLoadHistory();
        }
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        chatSetState('idle');
    } else if (type === 'paused') {
        // P6 中止——独立气泡提示（宿主文本；单向数据流：前端只渲染）+ seal 全部流式容器 + 复位 idle（已生成内容保留显示）
        chatPhaseReset();
        for (var kp in viewContainers) {
            var cp = viewContainers[kp];
            if (cp && cp.bubble) {
                cp.bubble.classList.remove('streaming');
                cp.bubble.classList.remove('streaming-wait');
            }
        }
        viewContainers = {};
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        var pb = chatBubble('assistant', 'paused');
        chatAppend(pb, payload.text || '已停止本轮（前文保留）');
        chatInfo.textContent = '已停止本轮（前文保留）';
        chatSetState('idle');
    } else if (type === 'note') {
        // 问题一修复——宿主经 view/control 通道推送 Note 状态（ChatSession.PushNoteState→PushView）；转交 noteOnEvent 重绘（chat-note.js）
        noteOnEvent(payload);
    }
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
    html += '<div class="rs-tok">↑' + chatFmtCount(d.prompt || 0)
        + ' ↓' + chatFmtCount(d.completion || 0)
        + ' cache ' + chatFmtCount(d.cacheHit || 0)
        + ' miss ' + chatFmtCount(miss) + '</div>';
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

// 新会话——session.new 指令（P8.5 design-ch4-workspace §六：清前文 + 按清单重新注入；走 CommandBus 无飞线）
// 问题一修复——本地不再抢调 chatLoadHistory（竞态根因：session.new 未处理完 history 返回旧块）；
// 置 chatPendingReset 标记，内核处理完推 session_reset 事件 → 前端统一清空+重建；miss 由 chatdone 兜底
function chatNewSession() {
    if (chatState === 'sending') { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'session.new' })
    }).catch(function () {});
    chatPendingReset = true;
    viewContainers = {};
    chatPhaseResetFull();
    chatMsgs.textContent = '';
    chatInfo.textContent = '新会话——注入中…';
}

// 停止本轮——cat.pause 指令（P6：已生成内容保留 + 前文格式修复，不裁剪；仅 sending 可用）
function chatPause() {
    if (chatState !== 'sending') { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'cat.pause' })
    }).catch(function () {});
}

// 刷新——纯前端重建界面气泡（重新拉历史渲染，不发指令）
function chatRefresh() {
    if (chatState === 'sending') { return; }
    chatLoadHistory();
}
