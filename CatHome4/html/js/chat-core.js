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
            + '<span class="tk">↑' + chatUsage.prompt + '</span>'
            + '<span class="tk c">↓' + chatUsage.completion + '</span>'
            + (chatUsage.cacheHit > 0 ? '<span class="tk ch">cache ' + chatUsage.cacheHit + '</span>' : '')
            + (miss > 0 ? '<span class="tk ms">miss ' + miss + '</span>' : '')
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
    // 思考块——流式实时展开可见；长思考默认收起（保留手动展开；F1 观感优化）
    var det = document.createElement('details');
    det.className = 'chat-reason';
    var sum = document.createElement('summary');
    sum.textContent = '思考过程';
    det.appendChild(sum);
    var pre = document.createElement('div');
    pre.textContent = text || '';
    det.appendChild(pre);
    det.open = !(text && text.length > 300);
    return det;
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
        } else if (blk.renderType === 'text') {
            var cb = chatBubble('assistant');
            cb.textContent = p.content || '';
        }
    }
    // P9.3 会话归属动态化——SSE sessionId 随会话 ID（时间戳）变化；history 先于任何 view 事件到达（loading→idle 时序保证）
    if (data.sessionId) {
        CHAT_SESSION = data.sessionId;
    }
    chatInfo.textContent = '会话 ' + (data.count || 0) + ' 条 | sessionId=' + CHAT_SESSION;
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
// 六种 renderType：user/stream/text/reason/toolcard/control
// stream 流式增量——seq 复用=同容器追加；新 seq=新建容器（text 与 reason 各自独立容器）
// text/reason 整块——replaceSeq 指向被替换的流式容器序号（流式→整块替换；无容器则新建）

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
    } else if (type === 'control') {
        chatOnControl(payload);
    }
    chatScrollBottom();
}

function chatOnStream(seq, payload) {
    chatKeepAlive();
    if (payload.kind === 'reasoning') {
        // 思考流式——独立气泡默认展开实时流式（B4.2）；首 reasoning 进入思考态
        if (!viewContainers[seq]) {
            var b = chatBubble('assistant', 'reason');
            b.classList.add('streaming');
            var det = chatReasonBlock('');
            b.appendChild(det);
            viewContainers[seq] = { type: 'reason', bubble: b, reasonPre: det.querySelector('div') };
            chatPhaseEnter('think');
        }
        var rc = viewContainers[seq];
        rc.reasonPre.textContent = rc.reasonPre.textContent + (payload.text || '');
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
    var content = payload.content || '';
    var c = viewContainers[replaceSeq];
    if (c && c.type === 'text') {
        c.bubble.classList.remove('streaming');
        c.bubble.classList.remove('streaming-wait');
        c.bubble.textContent = content;
        delete viewContainers[replaceSeq];
    } else {
        var b = chatBubble('assistant');
        b.textContent = content;
    }
    chatPhaseEnter('reply');
}

function chatOnReason(seq, replaceSeq, payload) {
    // 思考整块——replaceSeq≥0 且容器存在 → 替换思考流式容器；否则新建
    var content = payload.content || '';
    var c = viewContainers[replaceSeq];
    if (c && c.type === 'reason') {
        c.bubble.classList.remove('streaming');
        c.bubble.classList.remove('streaming-wait');
        c.reasonPre.textContent = content;
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
    var tb = chatBubble('assistant', 'tool');
    tb.appendChild(chatToolCard(payload));
    chatPhaseEnter('tool');
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
            chatInfo.textContent = '会话 ' + payload.count + ' 条 | sessionId=' + CHAT_SESSION;
        }
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        chatSetState('idle');
    }
}

// 新会话——session.new 指令（P8.5 design-ch4-workspace §六：清前文 + 按清单重新注入；走 CommandBus 无飞线）
function chatNewSession() {
    if (chatState === 'sending') { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'session.new' })
    }).catch(function () {});
    viewContainers = {};
    chatPhaseResetFull();
    chatMsgs.textContent = '';
    chatInfo.textContent = '新会话——注入中…';
    chatLoadHistory();
}

// 刷新——纯前端重建界面气泡（重新拉历史渲染，不发指令）
function chatRefresh() {
    if (chatState === 'sending') { return; }
    chatLoadHistory();
}
