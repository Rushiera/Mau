// CH4 外观层 v2——chat.js：对话区 + Note 面板（2026-08-25 拆分自 index.html）
// 依赖：app.js 先加载（chatState/chatMsgs/chatInput/chatBtn/chatInfo 全局变量 + SSE 事件分派）

// [段13] 对话区（B4）——阶段模型流式渲染
var CHAT_SESSION = 'majordomo';
var CHAT_TIMEOUT_MS = 120000;
var chatPending = [];              // 插话队列——本地发送记录（user 事件到达 FIFO 移除；纯展示）
var SYSTEM_AUTO_PREFIX = '[SystemAuto] ';   // 系统自动消息前缀（前端常量一处定义，微调只动此行）

function chatSetState(s) {
    chatState = s;
    // 单向数据流改造——忙时可插话（排队等本轮结束插入）；仅 loading 禁用
    chatInput.disabled = (s === 'loading');
    chatBtn.disabled = (s === 'loading');
    var nsb = document.getElementById('noteStartBtn');
    if (nsb) { nsb.disabled = (s !== 'idle'); }
}

function chatScrollBottom() {
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
    // 懒建时插到最前（insertBefore）——与历史渲染"文本在前、子元素在后"的顺序一致
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
            // 120s 无任何 llm/tool 事件——seal 当前阶段 + 错误提示 + 恢复 idle
            if (chatStage && chatStage.bubble) {
                chatStage.bubble.classList.remove('streaming');
                chatStage.bubble.classList.remove('streaming-wait');
                chatAppend(chatStage.bubble, '\n（LLM 无响应超过 120s——检查宿主控制台）');
                chatStage.bubble.classList.add('error');
            }
            chatStage = null;
            chatToolQueue = [];
            chatSetState('idle');
        }
    }, CHAT_TIMEOUT_MS);
}

function chatToolCard(tool) {
    var card = document.createElement('div');
    card.className = 'chat-tool';
    var name = document.createElement('div');
    name.className = 'tn';
    name.textContent = '🔧 ' + (tool.name || '?');
    card.appendChild(name);
    if (tool.arguments) {
        var a = document.createElement('div');
        a.className = 'ta';
        a.textContent = tool.arguments;
        card.appendChild(a);
    }
    if (tool.result) {
        var r = document.createElement('div');
        r.className = 'tr' + (tool.result.indexOf('ERR') === 0 ? ' err' : '');
        r.textContent = tool.result;
        card.appendChild(r);
    } else if (tool.result === undefined) {
        var w = document.createElement('div');
        w.className = 'ta';
        w.textContent = '⏳ 处理中…';
        card.appendChild(w);
    }
    return card;
}

function chatReasonBlock(text) {
    // 思考块——details 默认展开（流式实时可见）；用户可收起
    var det = document.createElement('details');
    det.className = 'chat-reason';
    det.open = true;
    var sum = document.createElement('summary');
    sum.textContent = '思考过程';
    det.appendChild(sum);
    var pre = document.createElement('div');
    pre.textContent = text || '';
    det.appendChild(pre);
    return det;
}

function chatRenderHistory(data) {
    // 阶段化历史渲染——与流式阶段模型同构：思考独立气泡 / 每工具独立气泡 / 回复独立气泡
    chatMsgs.textContent = '';
    var msgs = data.messages || [];
    for (var i = 0; i < msgs.length; i++) {
        var m = msgs[i];
        if (m.role === 'user') {
            var ub = chatBubble('user');
            ub.textContent = m.content || '';
            continue;
        }
        if (m.reasoning) {
            var rb = chatBubble('assistant', 'reason');
            rb.appendChild(chatReasonBlock(m.reasoning));
        }
        if (m.tools && m.tools.length) {
            for (var t = 0; t < m.tools.length; t++) {
                var tb = chatBubble('assistant', 'tool');
                tb.appendChild(chatToolCard(m.tools[t]));
            }
        }
        if (m.content) {
            var cb = chatBubble('assistant');
            cb.textContent = m.content;
        } else if (!m.reasoning && (!m.tools || !m.tools.length)) {
            var nb = chatBubble('assistant');
            nb.textContent = '（无文本回复）';
        }
    }
    chatInfo.textContent = '会话 ' + data.count + ' 条 | sessionId=' + (data.sessionId || CHAT_SESSION);
    // P9.3 会话归属动态化——SSE sessionId 随会话 ID（时间戳）变化；history 先于任何 llm 事件到达（loading→idle 时序保证）
    if (data.sessionId) {
        CHAT_SESSION = data.sessionId;
    }
    chatScrollBottom();
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
    // 发送失败——seal 当前阶段 + 独立错误气泡 + 恢复 idle
    chatSealCurrent();
    var eb = chatBubble('assistant');
    eb.textContent = msg;
    eb.classList.add('error');
    chatToolQueue = [];
    chatStage = null;
    if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
    // 发送失败——插话队列显示失去意义（内核未收到）
    chatPending = [];
    chatRenderPending();
    chatSetState('idle');
}

// 单向数据流改造——发送零自产气泡：只投递 command 总线，气泡由 SSE user 事件渲染
function chatSend() {
    var text = chatInput.value.trim();
    if (text.length === 0 || chatState === 'loading') { return; }
    chatInput.value = '';
    // 插话队列——本地记录（内核 user 事件到达后 FIFO 移除）
    chatPending.push(text);
    chatRenderPending();
    if (chatState === 'idle') {
        // idle 发送——清阶段残留（sending 态插话不清——不破坏当前流式渲染）
        chatStage = null;
        chatToolQueue = [];
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

// user 事件——内核确认消息进 Ctx 的唯一出口（单向数据流：前端气泡唯一来源）
function chatOnUser(d) {
    if (d.sessionId && d.sessionId !== CHAT_SESSION) { return; }
    // 插话队列 FIFO 移除——本地记录与内核确认对齐
    if (chatPending.length > 0) { chatPending.shift(); }
    chatRenderPending();
    var content = d.content || '';
    var source = d.source || 'user';
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

// 阶段模型（B4.2）——思考/工具/回复独立气泡；阶段切换时 seal 前一阶段
function chatSealCurrent() {
    if (!chatStage) { return; }
    if (chatStage.bubble) {
        chatStage.bubble.classList.remove('streaming');
        chatStage.bubble.classList.remove('streaming-wait');
        // 空阶段气泡（无文本无子元素）——移除（空气泡防御：LLM 在 tool_calls 前发空 content 块）
        if (chatStage.type !== 'tool' && !chatStage.bubble.textContent.trim() && !chatStage.bubble.querySelector('details, .chat-tool')) {
            var row = chatStage.bubble.parentNode;
            if (row && row.parentNode) {
                row.parentNode.removeChild(row);
            }
        }
    }
    chatStage = null;
}

function chatNewStage(type) {
    chatSealCurrent();
    var stage = { type: type, bubble: null, reasonPre: null };
    if (type === 'reason') {
        stage.bubble = chatBubble('assistant', 'reason');
        stage.bubble.classList.add('streaming');
        var det = chatReasonBlock('');
        stage.bubble.appendChild(det);
        stage.reasonPre = det.querySelector('div');
    } else if (type === 'text') {
        stage.bubble = chatBubble('assistant');
        stage.bubble.classList.add('streaming');
    }
    chatStage = stage;
    return stage;
}

// chatdone——会话终态：seal 所有阶段 / 未回填工具卡兜底标注 / 恢复 idle（llm done 仅一轮结束——终态唯一信号）
function chatOnChatDone(d) {
    if (chatState !== 'sending') {
        return;  // 非 sending（如重连后）——历史重绘已定型
    }
    chatSealCurrent();
    for (var ui = 0; ui < chatToolQueue.length; ui++) {
        if (!chatToolQueue[ui].filled) {
            var wu = document.createElement('div');
            wu.className = 'ta';
            wu.textContent = '（结果未回传——历史端点可查）';
            chatToolQueue[ui].card.appendChild(wu);
        }
    }
    chatToolQueue = [];
    chatStage = null;
    if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
    chatSetState('idle');
}

// tool 事件回填——宿主按执行序推送；前端按 FIFO 配对（事件序 = toolCalls 数组序）
function chatOnTool(d) {
    if (chatState !== 'sending') { return; }
    for (var i = 0; i < chatToolQueue.length; i++) {
        if (!chatToolQueue[i].filled) {
            chatFillToolCard(chatToolQueue[i], d);
            return;
        }
    }
}

function chatFillToolCard(slot, d) {
    slot.filled = true;
    slot.card.textContent = '';
    var name = document.createElement('div');
    name.className = 'tn';
    name.textContent = '🔧 ' + (d.name || slot.name || '?');
    slot.card.appendChild(name);
    if (d.arguments) {
        var a = document.createElement('div');
        a.className = 'ta';
        a.textContent = d.arguments;
        slot.card.appendChild(a);
    }
    if (d.result !== undefined && d.result !== null && d.result !== '') {
        var r = document.createElement('div');
        r.className = 'tr' + (String(d.result).indexOf('ERR') === 0 ? ' err' : '');
        r.textContent = String(d.result);
        slot.card.appendChild(r);
    } else {
        var w = document.createElement('div');
        w.className = 'ta';
        w.textContent = '（无结果文本）';
        slot.card.appendChild(w);
    }
    chatScrollBottom();
}

// llm 事件分发——对话区（sessionId 过滤 + 阶段模型五态渲染）
function chatOnLlm(d) {
    if (chatState !== 'sending') { return; }
    if (d.sessionId && d.sessionId !== CHAT_SESSION) { return; }
    chatKeepAlive();
    if (d.kind === 'reasoning') {
        // 思考阶段——独立气泡默认展开实时流式（B4.2）
        if (!chatStage || chatStage.type !== 'reason') {
            chatNewStage('reason');
        }
        chatStage.reasonPre.textContent = chatStage.reasonPre.textContent + (d.text || '');
    } else if (d.kind === 'text') {
        // 回复阶段——独立气泡流式；空增量（tool_calls 前的空 content 块）跳过——不建空气泡
        var t = d.text || '';
        if (t.length === 0) { return; }
        if (!chatStage || chatStage.type !== 'text') {
            chatNewStage('text');
        }
        chatStage.bubble.classList.remove('error');
        chatAppend(chatStage.bubble, t);
    } else if (d.kind === 'toolCalls') {
        // 工具阶段——seal 前一阶段；每个调用一个独立工具气泡（🔧 name + 参数 + 处理中…）
        chatSealCurrent();
        chatStage = { type: 'tool', bubble: null, reasonPre: null };
        var calls = [];
        try { calls = JSON.parse(d.text || '[]'); } catch (e) { calls = []; }
        for (var tc = 0; tc < calls.length; tc++) {
            var call = calls[tc] || {};
            var fn = call.function || {};
            var tb = chatBubble('assistant', 'tool');
            var card = chatToolCard({ name: fn.name, arguments: String(fn.arguments || '').substring(0, 200), result: undefined });
            tb.appendChild(card);
            chatToolQueue.push({ card: card, name: fn.name || '?', filled: false });
        }
        chatScrollBottom();
    } else if (d.kind === 'done') {
        // 一轮 LLM 流结束——停光标；stage 保留（续轮事件到来时 seal 切换；终态以 chatdone 为准）
        if (chatStage && chatStage.bubble) {
            chatStage.bubble.classList.remove('streaming');
            chatStage.bubble.classList.add('streaming-wait');
        }
    } else if (d.kind === 'error') {
        if (chatStage && chatStage.bubble) {
            chatStage.bubble.classList.remove('streaming');
            chatStage.bubble.classList.remove('streaming-wait');
            chatAppend(chatStage.bubble, '\n' + (d.text || 'LLM 错误'));
            chatStage.bubble.classList.add('error');
        }
        chatStage = null;
        chatToolQueue = [];
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        chatSetState('idle');
    }
    chatScrollBottom();
}

// 新会话——session.new 指令（P8.5 design-ch4-workspace §六：清前文 + 按清单重新注入；走 CommandBus 无飞线）
document.getElementById('chatNew').addEventListener('click', function () {
    if (chatState === 'sending') { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'session.new' })
    }).catch(function () {});
    chatMsgs.textContent = '';
    chatInfo.textContent = '新会话——注入中…';
    chatLoadHistory();
});

// 清空会话——session clear 指令
document.getElementById('chatClear').addEventListener('click', function () {
    if (chatState === 'sending') { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'session clear' })
    }).catch(function () {});
    chatMsgs.textContent = '';
    chatInfo.textContent = '会话已清空';
});

// 发送按钮与回车绑定
chatBtn.addEventListener('click', chatSend);
chatInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { chatSend(); }
});

// [段16] Note 面板（M4c——缩略一行/展开自适应；新增任务走 command 总线）
var noteState = { tasks: [], current: 0, done: 0 };
var noteExpanded = false;

function escapeHtml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function noteRender() {
    var tasks = noteState.tasks || [];
    var current = noteState.current || 0;
    var title = document.getElementById('noteTitle');
    var list = document.getElementById('noteList');
    if (tasks.length === 0) {
        title.textContent = 'Note · 空';
        list.innerHTML = '<div class="note-row" style="color:#5a5a5a">暂无计划</div>';
        return;
    }
    title.textContent = 'Note (' + (current + 1) + '/' + tasks.length + ')';
    var html = '';
    for (var i = 0; i < tasks.length; i++) {
        var cls = 'note-row';
        var mark = '[ ]';
        if (i < current) { cls = cls + ' done'; mark = '[x]'; }
        else if (i === current) { cls = cls + ' current'; mark = '[>]'; }
        html += '<div class="' + cls + '"><span class="mark">' + mark + '</span><span>' + escapeHtml(tasks[i]) + '</span></div>';
    }
    list.innerHTML = html;
}

function noteToggle() {
    noteExpanded = !noteExpanded;
    var panel = document.getElementById('notePanel');
    if (noteExpanded) { panel.classList.remove('collapsed'); document.getElementById('noteToggleBtn').textContent = '▾'; }
    else { panel.classList.add('collapsed'); document.getElementById('noteToggleBtn').textContent = '▸'; }
}

function noteAdd() {
    var input = document.getElementById('noteAddInput');
    var text = input.value.trim();
    if (text.length === 0) { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'note.add ' + text })
    }).then(function () { input.value = ''; });
}

function noteStart() {
    if (chatState !== 'idle') { return; }
    if (!noteState.tasks || noteState.tasks.length === 0) { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'note.start' })
    });
}

function noteOnEvent(d) {
    if (d.state) {
        noteState = d.state;
        // 手写/计划存在时自动展开面板（工作进度面板语义——人写入即弹出）
        if (noteState.tasks && noteState.tasks.length > 0 && !noteExpanded) {
            noteToggle();
        }
    }
    noteRender();
}

// Note 面板按钮绑定 + 页面加载兜底拉取
document.getElementById('noteHead').addEventListener('click', noteToggle);
document.getElementById('noteAddBtn').addEventListener('click', noteAdd);
document.getElementById('noteStartBtn').addEventListener('click', noteStart);
document.getElementById('noteAddInput').addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { noteAdd(); }
});
fetch('/api/v1/note').then(function (r) { return r.json(); }).then(function (j) { noteState = j; noteRender(); }).catch(function () {});
