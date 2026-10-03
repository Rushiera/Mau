// CH4 外观层——chat-core.js：对话核心（状态机 + view 事件分发 + 发送/新会话/暂停/刷新）——F2.3 自 chat.html 内联拆出（模块化；对话逻辑唯一真相源）
// 加载顺序：chat-md.js → chat-view.js → chat-core.js → chat-note.js（chat.html 引导层引用）；不依赖 app.js（自包含 DOM 引用——独立对话页无 app.js）
// 单向数据流铁律：SSE view/note 事件唯一渲染入口；发送走 command 总线；user 事件唯一气泡来源；前端零业务逻辑
// SSE/按钮绑定/初始化由 chat.html 引导层承接（applyUiConfig + EventSource view/note + 防御式 addEventListener + chatLoadHistory）
// 拆分（2026-09-08 体量治理）：渲染面（气泡/工具卡/注入报告/历史/roundsum）→ chat-view.js；本文件只保留状态与分发

// [段1] 对话区状态（B4 同构——自 index.html 提取；P9.3d 独立对话页；F4 迁 view 协议）
// 公共工具在 js/ui-common.js：escapeHtml（文本转义）+ uiWarn（失败可见化）——本文件不再重复定义
var chatMsgs = document.getElementById('chatMsgs');
var chatInfo = document.getElementById('chatInfo');
var chatInput = document.getElementById('chatSendInput');
var chatBtn = document.getElementById('chatSendBtn');
var chatState = 'loading';        // loading/idle/sending
var viewContainers = {};          // F4 视图容器——seq → {type:'text'|'reason', bubble, reasonPre}（流式容器；整块替换后删除）
var chatTimer = null;             // 静默兜底定时器（A77：只作明确展示——不改在途块状态）
var CHAT_SESSION = '';
var CHAT_TIMEOUT_MS = 120000;
var chatPending = [];              // 插话队列——本地发送记录（user 事件到达 FIFO 移除；纯展示）
var SYSTEM_AUTO_PREFIX = '[SystemAuto] ';   // 系统自动消息前缀（前端常量一处定义，微调只动此行）
var chatPendingReset = false;      // 会话重置待确认——chatNewSession 置位；session_reset/chatdone 消费（miss 兜底）
// A65 待发图片——粘贴即上传（后端缓存区），发送时投递路径列表；包裹格式与编号由后端组装（前端零业务规则）
var chatImages = [];               // [{ path: 绝对路径, url: 本地 blob 预览地址 }]
var chatImageUploading = 0;        // 上传中计数——>0 时发送动作等待（防投递半截列表）

// 状态栏前文信息（2026-10-02）——「前文 n 条」「前文 n tokens」「前文关键信息」三段可点击，点击开前文弹层（js/chat-ctx.js）
// 其它临时提示（回滚投递 / 已停止 / 加载失败）仍走 chatInfo.textContent 直写——下次数据帧本函数重建结构
var chatInfoState = { sid: '', count: 0, tokens: 0 };

// 结构化设置——history / chatdone / usage 三处统一入口（局部更新走 chatInfoPatch）
function chatInfoSet(sid, count, tokens) {
    chatInfoState.sid = sid || '';
    chatInfoState.count = Number(count) || 0;
    chatInfoState.tokens = Number(tokens) || 0;
    chatInfoRender();
}

// 局部更新——usage / chatdone 帧只改数值（结构被临时提示覆盖时自动重建）
function chatInfoPatch(count, tokens) {
    if (count !== undefined && count !== null && Number(count) >= 0) { chatInfoState.count = Number(count) || 0; }
    if (tokens !== undefined && tokens !== null && Number(tokens) > 0) { chatInfoState.tokens = Number(tokens) || 0; }
    chatInfoRender();
}

// 结构重建——两段可点击（条数 → 按条视图；tokens → 按 token 视图）
function chatInfoRender() {
    chatInfo.textContent = '';
    var c = document.createElement('span');
    c.id = 'chatCtxCount';
    c.className = 'ctx-link';
    c.title = '前文条数——点击查看前文条目';
    c.textContent = '前文 ' + chatFmtCount(chatInfoState.count) + ' 条';
    c.addEventListener('click', function (ev) { ev.stopPropagation(); chatCtxOpen('list'); });
    chatInfo.appendChild(c);
    if (chatInfoState.sid) {
        chatInfo.appendChild(document.createTextNode(' | sessionId=' + chatInfoState.sid));
    }
    if (chatInfoState.tokens > 0) {
        var t = document.createElement('span');
        t.id = 'chatCtxTokens';
        t.className = 'ctx-link';
        t.title = '前文长度（最近一次请求真实 prompt）——点击查看 token 明细';
        t.textContent = '前文 ' + chatFmtCount(chatInfoState.tokens) + ' tokens';
        t.addEventListener('click', function (ev) { ev.stopPropagation(); chatCtxOpen('tokens'); });
        chatInfo.appendChild(document.createTextNode(' | '));
        chatInfo.appendChild(t);
    }
    // 前文关键信息——本次会话的四部分内容（加载报告 / 用户消息 / 正式回复 / 轮结算；与旧会话留档同源）
    var k = document.createElement('span');
    k.id = 'chatKeyInfo';
    k.className = 'ctx-link';
    k.title = '前文关键信息——本次会话的加载报告 / 用户消息 / 正式回复 / 轮结算';
    k.textContent = '前文关键信息';
    k.addEventListener('click', function (ev) { ev.stopPropagation(); chatCtxOpen('key'); });
    chatInfo.appendChild(document.createTextNode(' | '));
    chatInfo.appendChild(k);
}

// A59——六态状态条（链路/等待/思考/工具/执行/回复）：数据源 = 后端权威运行态（快照 sessions 段 runState/runMs/requests）——计时单源在后端，前端只渲染不自算
var chatPhaseMeta = [
    { key: 'link', label: 'Link', icon: '🔗' },
    { key: 'wait', label: 'Wait', icon: '⏳' },
    { key: 'think', label: 'Think', icon: '🧠' },
    { key: 'tool', label: 'Tool', icon: '🔧' },
    { key: 'run', label: 'Run', icon: '⚙️' },
    { key: 'reply', label: 'Reply', icon: '💬' }
];
var chatRunState = { state: '', ms: {}, requests: 0 };   // 后端运行态快照（本猫会话条目——id 即猫 key；数据源 = SSE patch 推送，前端零轮询）

function chatRunStart() {
    // 活跃轮开始——清运行态（新轮从零计；数据源 = SSE sessionstate 推送，前端零轮询、零自算计时）
    chatRunState = { state: '', ms: {}, requests: 0 };
    chatRenderStatus();
}

function chatPhaseReset() {
    // 终态/失败——清运行态：本轮统计已由 roundsum 气泡承载 → 底部六态一并消失（A61）
    chatRunState = { state: '', ms: {}, requests: 0 };
    chatRenderStatus();
}

function chatPhaseResetFull() {
    // 新会话——运行态 + 轮级 usage 清零
    chatPhaseReset();
    chatRenderStatus();
}

// A61——后端轮次进行中判定（运行态数据源 = SSE sessionstate；供刷新/重连后恢复前端 sending 面）
function chatRunning() {
    var s = chatRunState.state || '';
    return s !== '' && s !== 'idle';
}

function chatOnSessionState(d) {
    // 运行态推送——本猫运行态块（服务端变化或新连接首帧才推；状态条唯一数据源——空闲期零推送、前端零轮询）
    if (!d || !d.sessionId) { return; }
    chatRunState = {
        state: d.runState || '',
        ms: d.runMs || {},
        requests: d.requests || 0
    };
    chatRenderStatus();
    // 进行中标记——全局态唯一出入口（态变化即同步气泡外观；与状态条/桌宠同源同时刻）
    chatSyncRunningMarks();
    // A61 刷新兜底——后端轮次在跑而本端非发送态（刷新/新连接丢失本地态）→ 恢复 sending（停止按钮/插话面一致）
    if (chatRunning() && chatState !== 'sending' && chatState !== 'loading') {
        chatSetState('sending');
        chatKeepAlive();
    }
}

function chatRenderStatus() {
    // 状态条渲染——六态完成后端时长（当前态高亮 + 呼吸动效）+ ⏱ 总 + 请求次数，整体居中
    // （2026-09-22：本轮 Token 统计已去——Token 信息归 roundsum 轮末块；轮结束整行清空）
    var bar = document.getElementById('chatStatus');
    if (!bar) { return; }
    var html = '';
    var ms = chatRunState.ms || {};
    var active = chatRunState.state || '';
    var totalMs = 0;
    for (var i = 0; i < chatPhaseMeta.length; i++) {
        var p = chatPhaseMeta[i];
        var v = ms[p.key] || 0;
        totalMs = totalMs + v;
        if (v <= 0 && active !== p.key) { continue; }
        var cls = (active === p.key) ? 'active' : '';
        html += '<span class="st ' + p.key + ' ' + cls + '">' + p.icon + ' ' + p.label + ' ' + chatFmtMs(v) + '</span>';
    }
    if (totalMs > 0) {
        html += '<span class="st total">⏱ All ' + chatFmtMs(totalMs) + '</span>';
    }
    if (chatRunState.requests > 0) {
        // A114 平均首 token 延迟——link 累计 ÷ 请求次数（实时态：含当前进行中的 link 等待）
        var apiText = '🔄 Api ' + chatRunState.requests;
        if ((ms.link || 0) > 0) {
            apiText += '（' + (ms.link / 1000 / chatRunState.requests).toFixed(2) + ' s /use）';
        }
        html += '<span class="st req">' + apiText + '</span>';
    }
    bar.innerHTML = html;
    // 桌宠——状态渲染汇聚点回调（chat-pet.js；未加载时静默跳过）
    if (typeof chatPetSync === 'function') { chatPetSync(); }
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
    // 继续——仅 idle 可用（与停止互斥；「前文非空」由后端复核并出声）
    var ctb = document.getElementById('chatContinue');
    if (ctb) { ctb.disabled = (s !== 'idle'); }
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

// 跟随合并（A143）——流式期间每增量一次强制布局是主线程最大开销源之一；
// 合并到一帧至多一次（rAF 不可用时同步执行，行为保真）
var chatScrollRaf = 0;

function chatScrollSoon() {
    if (typeof requestAnimationFrame !== 'function') {
        chatScrollBottom(false);
        return;
    }
    if (chatScrollRaf !== 0) {
        return;
    }
    chatScrollRaf = requestAnimationFrame(function () {
        chatScrollRaf = 0;
        chatScrollBottom(false);
    });
}

// 强制滚底（force 语义）——先取消挂起的跟随合并，避免随后又被拉回
function chatScrollBottomNow(force) {
    if (chatScrollRaf !== 0 && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(chatScrollRaf);
        chatScrollRaf = 0;
    }
    chatScrollBottom(force);
}

// 是否贴近底部——供「内容插入之前」取样（插入后新块自身高度会顶过阈值：多行 user 气泡 / 整块回复 /
// 工具卡等大块被误判为"用户已上翻"→ 不跟随，只延伸滚动条）
function chatNearBottom() {
    return (chatMsgs.scrollHeight - chatMsgs.scrollTop - chatMsgs.clientHeight) < 80;
}

// 跳到最下层——快捷按钮（无条件滚到底；与智能跟随判定互不影响——2026-09-17）
function chatJumpBottom() {
    chatMsgs.scrollTop = chatMsgs.scrollHeight;
}

// 静默兜底（A77 · C 方案 · 2026-09-22）——只作「明确展示」：宿主静默超过阈值时贴一条提示，
// 前端不自改态（不摘 pending / streaming 类 · 不清容器 · 不置 idle）、不假装收口——
// 超时判定与终态卡全交后端（工具单 120s / 工具批 150s 两闸）；宿主真死时前端如实提示即可。
// 提示随下一条 view 事件撤销（宿主仍在跑 → 真实结果到达即自愈，见 chatClearStall）。
function chatKeepAlive() {
    if (chatTimer) { clearTimeout(chatTimer); }
    chatTimer = setTimeout(function () {
        if (chatState === 'sending') { chatShowStall(); }
    }, CHAT_TIMEOUT_MS);
}

// 静默提示——独立气泡（不动任何在途容器）；同一次静默期只贴一条（去重）
function chatShowStall() {
    if (chatMsgs.querySelector('.chat-bubble.stall')) { return; }
    var b = chatBubble('assistant', 'stall');
    b.textContent = '⚠️ 已 ' + Math.round(CHAT_TIMEOUT_MS / 1000) + 's 未收到任何新事件——宿主可能已退出或卡死；若宿主仍在运行，本条提示会在下一条消息到达时自动消失';
    chatMsgs.scrollTop = chatMsgs.scrollHeight;
}

// 静默提示撤销——view 事件到达即撤（宿主复活自愈；不残留误报）
function chatClearStall() {
    var els = chatMsgs.querySelectorAll('.chat-bubble.stall');
    for (var i = 0; i < els.length; i = i + 1) {
        chatRemoveBubble(els[i]);
    }
}

// 进行中标记同步——**全局态唯一出入口**（莎 2026-09-22 定）：气泡的「进行中」外观一律由后端运行态驱动，
// 不再由各 view 事件路径自判（原实现：新建容器即加标记 + 各处 seal 撤标记 = 多出口、时机不一）。
// 映射：think → 思考块流式标识 · reply → 回复流式标识 · tool/run → 工具卡进行中 · 其余态 → 无标识。
// 调用面：态变化（chatOnSessionState）+ 容器创建（chatOnStream / chatOnToolCard）——同一实现，无第二处判定。
function chatSyncRunningMarks() {
    var state = chatRunState.state || '';
    for (var k in viewContainers) {
        var c = viewContainers[k];
        if (!c || !c.bubble) { continue; }
        var cls = '';
        if (c.type === 'thinkstream') {
            // A85——think 流式块（完成块不在容器表内，天然无进行中标记）
            if (state === 'think') { cls = 'live'; }
        } else if (c.type === 'text') {
            if (state === 'reply') { cls = 'streaming'; }
        } else if (c.type === 'toolcard') {
            if (state === 'tool' || state === 'run') { cls = 'pending'; }
        }
        c.bubble.classList.remove('streaming');
        c.bubble.classList.remove('pending');
        c.bubble.classList.remove('live');
        if (cls.length > 0) { c.bubble.classList.add(cls); }
    }
}

// ============ A84/A85——活跃块计时表（纯前端自算 · 1 秒粒度 · 单一表驱动两类块） ============
// 适用面：工具卡「运行中」占位行（A84——已运行时长）· think 流式块头行（A85——字符/行数/已输出时间/速率）
// 起点写在元素 data-start（与块同生命周期——先行卡与 think 流式块都不落盘，刷新即随块消失）；
// 1 秒表原地更新（文本一律从 data-start 与当前时刻重算，不是累加——隐藏/节流后不漂移）；
// 无活跃元素即停表（新会话 / 历史重绘清空 DOM 后自停，无需各清空点挂勾）；页面隐藏停表、恢复即补算
var chatLiveTimer = null;
var CHAT_LIVE_TICK_MS = 1000;
// 活跃计时元素——两类块各自的更新器按类分派（同一张表；选择器单一出口）
var CHAT_LIVE_SELECTOR = '.chat-tool .' + CHAT_PENDING_HOLD_CLS + ', .chat-think.stream .ct-head';

// 原地更新——按元素类分派：工具卡占位行（已运行时长）/ think 流式块头行（统计重算）
function chatLiveTick() {
    var nodes = chatMsgs.querySelectorAll(CHAT_LIVE_SELECTOR);
    if (nodes.length === 0) {
        chatLiveSyncTimer();
        return;
    }
    var now = Date.now();
    for (var i = 0; i < nodes.length; i = i + 1) {
        var el = nodes[i];
        var start = parseInt(el.getAttribute('data-start'), 10);
        if (isNaN(start)) {
            start = now;
            el.setAttribute('data-start', String(start));
        }
        if (el.classList.contains('ct-head')) {
            chatThinkHeadRefresh(el, now - start);
        } else {
            el.textContent = chatPendingHoldText(now - start);
        }
    }
}

// 计时器同步——有活跃元素且页面可见才跑表（空表 / 隐藏页零开销）
function chatLiveSyncTimer() {
    var need = (chatMsgs.querySelectorAll(CHAT_LIVE_SELECTOR).length > 0)
        && document.visibilityState !== 'hidden';
    if (need && chatLiveTimer === null) {
        chatLiveTimer = setInterval(chatLiveTick, CHAT_LIVE_TICK_MS);
    }
    if (!need && chatLiveTimer !== null) {
        clearInterval(chatLiveTimer);
        chatLiveTimer = null;
    }
}

// 起点登记——块新建后调用：块内全部活跃元素记同一时刻（并发多块各自独立计时）
function chatLiveMark(bubble) {
    if (!bubble) { return; }
    var nodes = bubble.querySelectorAll(CHAT_LIVE_SELECTOR);
    var now = Date.now();
    for (var i = 0; i < nodes.length; i = i + 1) {
        nodes[i].setAttribute('data-start', String(now));
    }
}

// 页面可见性——隐藏停表（省开销）；恢复即补算一次再续表（文本从 data-start 重算，不丢时长）
document.addEventListener('visibilitychange', function () {
    if (document.visibilityState === 'hidden') {
        if (chatLiveTimer !== null) {
            clearInterval(chatLiveTimer);
            chatLiveTimer = null;
        }
        return;
    }
    chatLiveTick();
    chatLiveSyncTimer();
});

// user 视图块——内核确认消息进 Ctx 的唯一出口（单向数据流：前端气泡唯一来源）
function chatOnUser(payload) {
    // 插话队列 FIFO 移除——本地记录与内核确认对齐
    if (chatPending.length > 0) { chatPending.shift(); }
    chatRenderPending();
    var content = payload.content || '';
    var source = payload.source || 'user';
    var isSystem = (source === 'system');
    // 来源前缀——系统自动 [SystemAuto] / 延迟队列 ⏰ 定时 · 💤 唤醒 · 🔄 回执（design-ch4-delay §六）
    var prefix = '';
    if (isSystem) { prefix = SYSTEM_AUTO_PREFIX; }
    else if (source === 'delay') { prefix = '⏰ 定时 · '; }
    else if (source === 'sleep') { prefix = '💤 唤醒 · '; }
    else if (source === 'timer') { prefix = '⏳ 定时注入 · '; }
    else if (source === 'systemauto') { prefix = '⚙️ 系统 · '; }
    else if (source === 'restart') { prefix = '🔄 回执 · '; }
    var display = prefix + content;
    var bubble = chatBubble('user');
    if (source !== 'user') { bubble.classList.add('system'); }
    // A65 图片包裹——命中即缩略图组 + 正文；无包裹走原路径（textContent 零回归）
    chatUserFill(bubble, display);
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

// ============ A162 状态推送分发（快照模型） ============
// full——连接建立时服务端推一次（网页刷新 / 断线重连同一路径）：清容器后整体重绘
// delta——帧轮增量：blocks 逐块按块键应用（同键覆盖）+ remove 逐键删容器；无变化服务端零字节
// control——阶段控制（usage / chatdone / paused / note / session_reset）
// 旧 op 面（live.add / live.update / live.remove / persist.append）随 A162 退役

function chatOnView(d) {
    // A162 状态推送——loading 态放行全量帧（全量即服务端送达的初始状态；其余事件待就绪后再收）
    if (chatState === 'loading' && d.op !== 'full') { return; }
    // A77 静默提示撤销——view 事件到达即撤（宿主复活自愈；不残留误报）
    chatClearStall();
    // 跟随判定取样——「新内容到达前」是否贴近底部（新块自身高度不计入判定：多行 user 气泡 / 整块回复等
    // 大块会把插入后的距底距离顶过 80px 阈值 → 被误判为用户已上翻 → 不跟随、只延伸滚动条——2026-09-17 修复）
    var stickBottom = chatNearBottom();
    var op = d.op;
    if (op === 'full') {
        chatOnViewFull(d);
    } else if (op === 'delta') {
        chatOnViewDelta(d);
    } else if (op === 'control') {
        chatOnControl(d.payload || {});
    }
    // 末尾按取样结果滚动——stickBottom（到达前已贴近底部）则维持在最下层；否则尊重用户上翻位置不动
    if (stickBottom) { chatMsgs.scrollTop = chatMsgs.scrollHeight; }
}

// 全量——清空流式容器与对话区后整体重绘（全量的唯一入口：连接建立时服务端推送）
// A96——从尾往头渐进：尾部窗口先出（最新内容最先可见 + 立即贴底），其余逐帧前插补齐（不打断阅读）
// A97——状态栏前文三段随全量帧一并到位（刷新 / 重连后「前文 n 条 / n tokens / 前文关键信息」直接可见）
function chatOnViewFull(d) {
    chatClearLiveRegion();
    chatRenderFullProgressive(d.blocks || []);
    if (d.sessionId) { CHAT_SESSION = d.sessionId; }
    if (d.ctxCount !== undefined && d.ctxCount !== null) {
        chatInfoSet(d.sessionId || CHAT_SESSION, d.ctxCount, d.ctxTokens || 0);
    }
    chatSyncRunningMarks();
    chatLiveSyncTimer();
    // A162——全量帧即初始状态就绪：退出 loading 态（此前由 chatLoadHistory 收尾置位，纯化后改由此处收口）
    if (chatState === 'loading') { chatSetState('idle'); }
}

// 增量——blocks 逐块应用（同键覆盖）+ remove 逐键删容器
function chatOnViewDelta(d) {
    var blocks = d.blocks || [];
    for (var i = 0; i < blocks.length; i++) {
        chatApplyBlock(blocks[i]);
    }
    var remove = d.remove || [];
    for (var j = 0; j < remove.length; j++) {
        chatOnLiveRemove(remove[j]);
    }
    chatSyncRunningMarks();
    chatLiveSyncTimer();
}

// 单块应用——按块键定位容器：流式类（stream / toolcard）走实时渲染，其余走历史块渲染
function chatApplyBlock(b) {
    var key = b.key || '';
    var payload = b.payload || {};
    if (b.renderType === 'stream') {
        chatOnLiveStream(key, payload);
        return;
    }
    if (b.renderType === 'toolcard') {
        chatOnLiveToolCard(key, payload);
        return;
    }
    if (b.renderType === 'user') {
        // 进内核消息——保留实时语义（source 前缀 + 插话队列确认）
        chatOnUser(payload);
        return;
    }
    chatAppendHistoryBlock(b);
    // A55——错误块到达即恢复 idle（旧 chatOnError 的收尾语义）
    if (b.renderType === 'error') { chatSetState('idle'); }
}

// 流式区移除——区里没了就删气泡（delta 的 remove 表达）
function chatOnLiveRemove(key) {
    var c = viewContainers[key];
    if (!c) { return; }
    delete viewContainers[key];
    if (c.bubble) { chatRemoveBubble(c.bubble); }
    chatLiveSyncTimer();
    chatSyncRunningMarks();
}

// 流式增量（文本 / 思考）——按块键建容器或追加（空增量不建气泡）
function chatOnLiveStream(key, payload) {
    chatKeepAlive();
    if (payload.kind === 'reasoning') {
        // A85——think 流式态（live-only）：首帧建块；终结由后端 live.remove 驱动（前端不自判）
        if (!viewContainers[key]) {
            var rb = chatBubble('assistant', 'reason');
            var tk = chatThinkStream();
            rb.appendChild(tk.box);
            viewContainers[key] = { type: 'thinkstream', bubble: rb, head: tk.head, body: tk.body, cursor: tk.cursor };
            // 起点登记——头行 data-start（活跃计时表按此时刻算已输出时长与速率）
            chatLiveMark(rb);
        }
        var rc = viewContainers[key];
        chatThinkAppend(rc.body, rc.cursor, payload.text || '');
        chatThinkScroll(rc.body);
        // 头行实时刷新——与 1 秒表同一出口（增量到达即更新，不等下一秒）
        chatLiveTick();
        return;
    }
    // 回复流式——独立气泡流式；空增量（tool_calls 前的空 content 块）跳过——不建空气泡
    var t = payload.text || '';
    if (t.length === 0) { return; }
    if (!viewContainers[key]) {
        var tb = chatBubble('assistant');
        viewContainers[key] = { type: 'text', bubble: tb };
    }
    var tc = viewContainers[key];
    tc.bubble.classList.remove('error');
    chatAppend(tc.bubble, t);
}

// 工具卡（流式区）——先行卡与终态卡同在块键容器内换卡（后端 op 决定时机，前端不做配对）
function chatOnLiveToolCard(key, payload) {
    chatKeepAlive();
    var pending = (payload.result === undefined);
    var c = viewContainers[key];
    if (c && c.type === 'toolcard') {
        var replaced = chatToolCard(payload, pending);
        c.bubble.replaceChild(replaced, c.card);
        c.card = replaced;
        if (!pending) { c.bubble.classList.remove('pending'); }
        return;
    }
    var tb = chatBubble('assistant', 'tool');
    var card = chatToolCard(payload, pending);
    tb.appendChild(card);
    viewContainers[key] = { type: 'toolcard', bubble: tb, card: card };
    if (pending) {
        // A84——登记已运行时长起点（卡内占位元素 data-start）并保证计时表在跑
        chatLiveMark(tb);
    }
    // A158——函数自足收尾（旧 chatOnToolCard 同源：直调路径也须同步进行中标记与计时表）
    chatSyncRunningMarks();
    chatLiveSyncTimer();
}

// A55——重试气泡文本（渲染单例内文本面）；A86——resolved 不覆盖报错信息：原文保留 + 追加「已恢复」
// A94——三态齐备：retrying（⟳ 重试中 N/3 · 原因）/ resolved（原文 + ✓ 已恢复）/ failed（原文 + ⚠ 重试失败）
function chatRetryText(payload) {
    var state = payload.state || 'retrying';
    var attempt = payload.attempt || '';
    var max = payload.max || '';
    var reason = payload.text || '';
    if (state === 'failover') {
        // 端点切换提示（备用轮）——role=切换后角色（主要 / 备用）；reason 空=手动对调
        var foRole = payload.role || '备用';
        var foTail = reason ? (' · ' + reason) : '';
        return '⇄ 已切换端点：' + foRole + '站' + foTail;
    }
    var bar = attempt + (max ? '/' + max : '');
    var tail = reason ? (' · ' + reason) : '';
    if (state === 'failed') {
        return '⚠ 重试失败 ' + bar + tail;
    }
    return '⟳ 重试中 ' + bar + tail + (state === 'resolved' ? ' ✓ 已恢复' : '');
}

// A94——retry 气泡状态类单一出口（resolved / failed 终态；retrying 无附加类）
function chatApplyRetryState(bubble, payload) {
    var state = payload.state || 'retrying';
    bubble.classList.toggle('resolved', state === 'resolved');
    bubble.classList.toggle('failed', state === 'failed');
}

// A55——重试渲染单例：新建重试气泡（历史重建与实时事件共用同一渲染面）
function chatRenderRetry(payload) {
    var b = chatBubble('assistant', 'retry');
    b.textContent = chatRetryText(payload);
    chatApplyRetryState(b, payload);
    return b;
}

// A55——错误渲染单例：新建错误气泡（历史重建与实时事件共用同一渲染面）
function chatRenderError(text) {
    var eb = chatBubble('assistant');
    eb.textContent = text || 'LLM 错误';
    eb.classList.add('error');
    return eb;
}

// A55——错误事件（renderType=error）：seal 全部流式容器（已生成内容保留，不追加错误文本）+
// 恒定新建独立错误气泡——错误与重试解耦（重试气泡只表达重试过程；莎 2026-09-16 拍板 A）
function chatOnControl(payload) {
    var type = payload.type;
    if (type === 'usage') {
        // 2026-09-22：本轮 Token 统计已从状态条撤除（Token 信息归 roundsum 轮末块）——此处只保留前文长度实时化
        // 2026-10-02：状态栏前文两段改结构化（可点击开弹层）——数值更新统一走 chatInfoPatch
        var u = payload.data || {};
        chatInfoPatch(u.count, u.context);
    } else if (type === 'session_reset') {
        // 会话重置——session.new 清前文后显式信号（问题一修复：消除本地抢跑竞态；收到即清空再拉 history）
        chatPendingReset = false;
        viewContainers = {};
        chatPhaseResetFull();
        chatMsgs.textContent = '';
        chatImagesClear();   // A65——会话重置后待发图片清空（前序语境已变）
        chatInfo.textContent = '新会话——注入完成，重建中…';
        chatLoadHistory();
    } else if (type === 'chatdone') {
        // 会话终态——seal 全部流式容器 + 未回填兜底已由 toolcard 整块覆盖 + 恢复 idle
        chatPhaseReset();
        for (var k2 in viewContainers) {
            var c2 = viewContainers[k2];
            if (c2 && c2.bubble) {
                c2.bubble.classList.remove('streaming');
            }
        }
        viewContainers = {};
        if (payload.count !== undefined) {
            var ds = payload.stats;
            // 前文长度 = 最近一次请求的单次 prompt（context 字段）；旧数据无 context 时回退累计值
            var ctx2 = ds ? ((ds.context !== undefined && ds.context > 0) ? ds.context : (ds.prompt || 0)) : 0;
            chatInfoPatch(payload.count, ctx2);
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
            }
        }
        viewContainers = {};
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
        var pb = chatBubble('assistant', 'paused');
        chatAppend(pb, payload.text || '已停止本轮（前文保留）');
        chatInfo.textContent = '已停止本轮（前文保留）';
        chatSetState('idle');
    } else if (type === 'autocontinue') {
        // 端点切换后自动继续——提示气泡 + 进入新一轮 sending 态（后端已置继续请求，前端只跟进状态，不重发指令）
        chatPhaseReset();
        chatClearLiveThink();
        for (var ka in viewContainers) {
            var ca = viewContainers[ka];
            if (ca && ca.bubble) {
                ca.bubble.classList.remove('streaming');
            }
        }
        viewContainers = {};
        var ab = chatBubble('assistant', 'paused');
        chatAppend(ab, payload.text || '端点切换后自动继续');
        chatInfo.textContent = payload.text || '';
        chatRunStart();
        chatSetState('sending');
        chatKeepAlive();
    } else if (type === 'note') {
        // 问题一修复——宿主经 view/control 通道推送 Note 状态（ChatSession.PushNoteState→PushView）；转交 noteOnEvent 重绘（chat-note.js）
        noteOnEvent(payload);
    }
}

// A95——渐进渲染参数（design-ch4-frontend-history §三）
// 首屏尾部窗口（最新内容最先出现）+ 每帧往前补齐的块数
var CHAT_HISTORY_FIRST = 60;
var CHAT_HISTORY_STEP = 60;
// 补齐代际——每次加载递增；在途补齐发现代际变化即自行作废（会话切换 / 刷新不画出上一会话的块）
var chatBackfillGen = 0;

// A158——流式区快照重建（全量加载时消费 history 的 live 数组）
function chatClearLiveRegion() {
    for (var k in viewContainers) {
        var c = viewContainers[k];
        if (c && c.bubble) { chatRemoveBubble(c.bubble); }
    }
    viewContainers = {};
    chatLiveSyncTimer();
}

/**
 * 全量历史加载——清空重建（首连 / 重连 / 会话切换一律走此口——顶层数据流：连接建立即拉一次全量）
 * 后端一次回完整块序列（A95——零分页）；渲染侧先出尾部窗口，其余逐帧往前补齐（chat-view.chatRenderHistory）
 */
function chatLoadHistory() {
    fetch('/api/v1/history')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            // 流式区先清——快照随后重建（后端区里有什么就显示什么）
            chatClearLiveRegion();
            // 会话归属落库——渲染层只渲染并返回 sessionId（P20-P3-7：渲染层不写全局状态）
            var sid = chatRenderHistory(d);
            if (sid) { CHAT_SESSION = sid; }
            var live = d.live || [];
            for (var i = 0; i < live.length; i++) { chatApplyBlock(live[i]); }
            // A61 刷新兜底——后端轮次仍在跑（运行态已由 sessionstate 首帧送达）→ 保持 sending（停止按钮可用）
            chatSetState(chatRunning() ? 'sending' : 'idle');
        })
        .catch(function () {
            chatInfo.textContent = '历史加载失败——宿主未运行？';
            chatSetState('idle');
        });
}

/**
 * 补齐调度——有 rAF 走帧回调（每帧一批，不阻塞交互）；无 rAF 环境同步补齐（测试 / 老浏览器——行为保真）
 * @param {Function} fn 一帧动作
 */
function chatBackfillSchedule(fn) {
    if (typeof requestAnimationFrame === 'function') {
        requestAnimationFrame(fn);
        return;
    }
    fn();
}

/**
 * 历史补齐——首屏之后的更早块逐帧前插（保位，不打断阅读）
 * 语义：一次加载一次补齐；补齐期间发生新加载（代际变化）→ 在途补齐自行作废
 * @param {Array} blocks 全量块序列
 * @param {number} from 首屏窗口起点（该序之前的块待补齐）
 * @param {Function} [apply] 单块渲染函数（缺省 = chatAppendHistoryBlock；全量帧路径传 chatApplyBlock）
 */
function chatHistoryBackfill(blocks, from, apply) {
    chatBackfillGen = chatBackfillGen + 1;
    var gen = chatBackfillGen;
    var idx = from;
    var render = apply || chatAppendHistoryBlock;
    function step() {
        if (gen !== chatBackfillGen) { return; }
        if (idx <= 0) { return; }
        var next = (idx > CHAT_HISTORY_STEP) ? (idx - CHAT_HISTORY_STEP) : 0;
        chatPrependBlocks(blocks.slice(next, idx), render);
        idx = next;
        chatBackfillSchedule(step);
    }
    chatBackfillSchedule(step);
}


function chatFail(msg) {
    // E 系列——失败：四态状态条清零隐藏
    chatPhaseReset();
    // 发送失败——seal 全部流式容器 + 独立错误气泡 + 恢复 idle
    for (var k in viewContainers) {
        var c = viewContainers[k];
        if (c && c.bubble) {
            c.bubble.classList.remove('streaming');
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

// ============ A65 待发图片——粘贴上传 / 待发区 / 清空（前端只上传+显示+投递路径） ============
function chatImgPreviewUrl(file) {
    // 本地预览地址——blob URL；环境不支持（或对象类型不兼容）时回落空串（预览降级，上传与投递不受影响）
    try {
        if (typeof URL !== 'undefined' && typeof URL.createObjectURL === 'function') {
            return URL.createObjectURL(file);
        }
    } catch (e) {
        return '';
    }
    return '';
}

function chatImgRevokeUrl(url) {
    // 预览地址释放——环境不支持时静默跳过
    if (url && typeof URL !== 'undefined' && typeof URL.revokeObjectURL === 'function') {
        URL.revokeObjectURL(url);
    }
}

function chatOnPaste(e) {
    // 粘贴拦截——仅图片项；纯文本粘贴不拦截（原样入框）
    var dt = e.clipboardData;
    if (!dt) { return; }
    var files = [];
    var items = dt.items || [];
    for (var i = 0; i < items.length; i++) {
        if (items[i].kind === 'file' && items[i].type && items[i].type.indexOf('image/') === 0) {
            var f = items[i].getAsFile();
            if (f) { files.push(f); }
        }
    }
    if (files.length === 0) { return; }
    e.preventDefault();
    for (var k = 0; k < files.length; k++) { chatUploadImage(files[k]); }
}

function chatUploadImage(file) {
    // 逐张上传——成功后入待发区（路径供投递，blob 供本地预览）；失败给可见提示且不入列（防死路径进包裹）
    chatImageUploading = chatImageUploading + 1;
    chatRenderImages();
    fetch('/api/v1/chat-images', {
        method: 'POST',
        headers: { 'Content-Type': file.type || 'image/png' },
        body: file
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            chatImageUploading = chatImageUploading - 1;
            if (!d || !d.ok || !d.path) {
                // 失败可见化——先重绘（清空区）再挂提示，避免被重绘冲掉
                chatRenderImages();
                chatImageNotice((d && d.error) ? d.error : '图片上传失败');
                return;
            }
            chatImages.push({ path: d.path, url: chatImgPreviewUrl(file) });
            chatRenderImages();
        })
        .catch(function (err) {
            chatImageUploading = chatImageUploading - 1;
            chatRenderImages();
            chatImageNotice('图片上传失败: ' + err);
        });
}

function chatImageNotice(msg) {
    // 失败可见化——待发区尾部提示（3 秒自动消失；不阻断）
    var box = document.getElementById('chatImages');
    if (!box) { return; }
    var d = document.createElement('span');
    d.className = 'chat-img-err';
    d.textContent = msg;
    box.appendChild(d);
    window.setTimeout(function () {
        if (d.parentNode) { d.parentNode.removeChild(d); }
    }, 3000);
}

function chatRenderImages() {
    // 待发区渲染——本地 blob 预览（零服务端往返）+ 移除按钮 + 上传中占位
    var box = document.getElementById('chatImages');
    if (!box) { return; }
    box.textContent = '';
    for (var i = 0; i < chatImages.length; i++) { box.appendChild(chatImgPendingItem(chatImages[i], i)); }
    for (var k = 0; k < chatImageUploading; k++) {
        var w = document.createElement('span');
        w.className = 'chat-img-loading';
        w.textContent = '上传中…';
        box.appendChild(w);
    }
}

function chatImgPendingItem(item, index) {
    var fig = document.createElement('figure');
    fig.className = 'chat-img';
    var img = document.createElement('img');
    img.src = item.url;
    img.alt = '待发图片';
    var del = document.createElement('button');
    del.type = 'button';
    del.className = 'chat-img-del';
    del.title = '移除这张图片（不发送）';
    del.textContent = '×';
    del.addEventListener('click', function () { chatImagesRemove(index); });
    fig.appendChild(img);
    fig.appendChild(del);
    return fig;
}

function chatImagesRemove(index) {
    if (index < 0 || index >= chatImages.length) { return; }
    var it = chatImages[index];
    if (it) { chatImgRevokeUrl(it.url); }
    chatImages.splice(index, 1);
    chatRenderImages();
}

function chatImagesClear() {
    for (var i = 0; i < chatImages.length; i++) {
        if (chatImages[i]) { chatImgRevokeUrl(chatImages[i].url); }
    }
    chatImages = [];
    chatRenderImages();
}

function chatImagePaths() {
    // 待发图片路径列表——投递载荷（编号与包裹由后端组装）
    var paths = [];
    for (var i = 0; i < chatImages.length; i++) { paths.push(chatImages[i].path); }
    return paths;
}

// 单向数据流改造——发送零自产气泡：只投递 command 总线，气泡由 view user 事件渲染
function chatSend() {
    var text = chatInput.value.trim();
    if (chatState === 'loading') { return; }
    if (chatImageUploading > 0) {
        chatInfo.textContent = '图片上传中——稍候再发';
        return;
    }
    if (text.length === 0 && chatImages.length === 0) { return; }
    var paths = chatImagePaths();
    chatInput.value = '';
    chatImagesClear();
    // 插话队列——本地记录（内核 user 事件到达后 FIFO 移除）
    chatPending.push(text.length > 0 ? text : '（图片）');
    chatRenderPending();
    if (chatState === 'idle') {
        // idle 发送——清阶段残留（usage 会话累计保留——跨轮不清零；sending 态插话不清——不破坏当前流式渲染）
        viewContainers = {};
        // A59——启动后端运行态拉取（发送即活跃轮；插话轮沿用已启动的拉取）
        chatRunStart();
    }
    chatSetState('sending');
    chatKeepAlive();
    var payload = { text: 'Chat ' + text };
    if (paths.length > 0) { payload.images = paths; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok && d.error) {
                chatFail('发送失败: ' + d.error);
            }
        })
        .catch(function (err) { chatFail('请求失败: ' + err); });
}

// 端点角色标签——[API 主要[模型]] / [API 备用[模型]]（只显当前生效站；会话级：自动故障转移 180 秒窗口 / 手动对调）
// 数据源：GET /api/v1/api-role（对话端口注入槽，含两站模型名）；点击 → POST /api/v1/api-role/toggle
function chatApiRoleRender(info) {
    var btn = document.getElementById('chatApiRole');
    if (!btn) { return; }
    var role = (info && info.role) ? info.role : '主要';
    var model = '—';
    if (role === '备用') {
        model = (info && info.backup) ? info.backup : '—';
        btn.classList.add('backup');
    } else {
        model = (info && info.primary) ? info.primary : '—';
        btn.classList.remove('backup');
    }
    btn.textContent = 'API ' + role + '[' + model + ']';
}

function chatApiRoleRefresh() {
    fetch('/api/v1/api-role')
        .then(function (r) { return r.json(); })
        .then(function (d) { chatApiRoleRender(d); })
        .catch(function () { /* 端点缺失（旧宿主 / 管理端口）——标签保持默认，不报错 */ });
}

function chatApiRoleToggle() {
    fetch('/api/v1/api-role/toggle', { method: 'POST' })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (d && d.ok) {
                // 重拉信息面——切换后角色与两站模型名一并刷新（toggle 回执只带角色）
                chatApiRoleRefresh();
                return;
            }
            uiWarn('端点角色切换', new Error((d && d.error) || '未生效'));
        })
        .catch(function (e) { uiWarn('端点角色切换', e); });
}

// 新会话——session.new 指令（P8.5 design-ch4-workspace §六：清前文 + 按清单重新注入；走 CommandBus 无飞线）
// 问题一修复——本地不再抢调 chatLoadHistory（竞态根因：session.new 未处理完 history 返回旧块）；
// 置 chatPendingReset 标记，内核处理完推 session_reset 事件 → 前端统一清空+重建；miss 由 chatdone 兜底
function chatNewSession() {
    if (chatState === 'sending') { return; }
    if (!window.confirm('开启新会话？当前对话前文将被清空并重新注入。')) { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'session.new' })
    }).catch(function (e) { uiWarn('新会话指令投递', e); });
    chatPendingReset = true;
    chatImagesClear();   // A65——待发图片随会话切换清空（重新注入前文，前序语境已变）
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
    }).catch(function (e) { uiWarn('停止指令投递', e); });
}

// 继续——cat.continue 指令（不追加消息：直接用当前前文再发一次 LLM 请求；仅 idle 可用，与停止互斥）
// 与 chatSend 的 idle 起始面同构（清阶段残留 + 运行态拉取 + sending + 守护），差别仅在载荷与不产生 user 气泡
function chatContinue() {
    if (chatState !== 'idle') { return; }
    viewContainers = {};
    chatRunStart();
    chatSetState('sending');
    chatKeepAlive();
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'cat.continue' })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok && d.error) {
                chatFail('继续失败: ' + d.error);
            }
        })
        .catch(function (err) { chatFail('请求失败: ' + err); });
}

// 刷新——纯前端重建界面气泡（重新拉历史渲染，不发指令）
function chatRefresh() {
    if (chatState === 'sending') { return; }
    chatLoadHistory();
}
