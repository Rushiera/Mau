// ═══════════════════════════════════════════
// chat/main.js —— 对话页主干入口（契约 §12.1 / §12.3 · §6.1 L）
//
// 定位：**单一应用入口**。一个 SSE 流收包 → 三段各归各位——
//       state（状态投影）· persist（持久对话区）· live（临时区）。
//       **无事件分派、无键算术、无配对、无排序**（顺序 = 推送顺序）。
//
// 加载顺序（chat.html 引导层）：
//   lib/*.js → blocks/*.js → registry.js → persist.js → live.js → state.js → main.js → 侧翼件
//
// 边界：本件承担「收包 + 滚动跟随 + 公共小件」；块渲染归素材，状态投影归 state.js。
// ═══════════════════════════════════════════

/// SSE 连接句柄——页面生命周期内单连接（断线由浏览器自动重连，重连即服务端再推全量帧）
var chatSse = null;

/// 顶部信息位（#chatInfo）唯一写入口——**state 段专属**（后端必要信息：前文条数 / sessionId / 前文长度 / 关键信息）
/// 入参：段数组（字符串 = 文本段；元素 = 段节点，如可点击的前文段——点击处理归 ctx.js）——数组序即呈现序
/// 归位（A179 收尾）：前端告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本处不再有竞写
function chatInfoSet(parts) {
    var info = document.getElementById('chatInfo');
    if (!info) {
        return;
    }
    var list = (parts && parts.length !== undefined && typeof parts !== 'string') ? parts : [parts];
    info.textContent = '';
    for (var i = 0; i < list.length; i = i + 1) {
        var p = list[i];
        if (p === null || p === undefined) { continue; }
        if (typeof p === 'string') {
            info.appendChild(document.createTextNode(p));
            continue;
        }
        info.appendChild(p);
    }
}

/// 失败可见——控制台出声 + 前端通知面（桌宠气泡；AGENTS 底线四——不静默吞）
/// 归位（A179 收尾）：告警不写顶栏信息位——那是 state 段的后端信息输出口
function warn(msg, err) {
    if (typeof console !== 'undefined' && console.warn) {
        console.warn('[chat] ' + msg, err || '');
    }
    if (typeof chatPetSay === 'function') {
        chatPetSay('⚠ ' + msg);
    }
}

// ── 滚动跟随（对话区）──────────────────────────
/// 跟随合并句柄——流式期间每增量一次强制布局是主线程最大开销源之一，合并到一帧至多一次
var scrollRaf = 0;

/// 智能跟随——贴近底部才滚（用户上翻读历史时不打扰）；合并到一帧至多一次
function scrollSoon() {
    if (typeof requestAnimationFrame !== 'function') {
        scrollBottomNow(false);
        return;
    }
    if (scrollRaf !== 0) {
        return;
    }
    scrollRaf = requestAnimationFrame(function () {
        scrollRaf = 0;
        scrollBottomNow(false);
    });
}

/// 滚底——force=true 无条件滚底（全量重绘 / 新会话）；否则仅贴近底部时跟随
function scrollBottomNow(force) {
    var box = persistContainer();
    if (!box) {
        return;
    }
    if (scrollRaf !== 0 && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(scrollRaf);
        scrollRaf = 0;
    }
    if (force !== true) {
        var near = box.scrollHeight - box.scrollTop - box.clientHeight;
        if (near >= 80) {
            return;
        }
    }
    box.scrollTop = box.scrollHeight;
}

/// 无条件跳底——快捷按钮（用户显式要求回到最新内容）
function jumpBottom() {
    var box = persistContainer();
    if (box) {
        box.scrollTop = box.scrollHeight;
    }
}

// ── 单一应用入口 ──────────────────────────────
/// 收包——三段各归各位（段可单独出现：追加帧 / 变化增量帧 / 全量帧）
function chatOnFrame(json) {
    if (!json) {
        return;
    }
    if (json.state) {
        stateApply(json.state);
    }
    if (json.persist) {
        persistApply(json.persist);
    }
    if (json.live) {
        liveApply(json.live);
    }
}

/// 帧解析——解析失败出声并丢弃本帧（不中断连接）
function chatParseFrame(data) {
    try {
        return JSON.parse(data);
    } catch (e) {
        warn('帧 JSON 解析失败', e);
        return null;
    }
}

// ── 连接 ──────────────────────────────────────
/// 建立 SSE——chat 面唯一推送通道；首帧全量由服务端在连接建立时送达（前端零请求）
function chatConnect() {
    if (typeof EventSource !== 'function') {
        warn('本浏览器不支持 EventSource');
        return;
    }
    // A181——只订 chat 面事件（管理面事件 snapshot/patch/log/cmd 与本面无监听器、契约 §12.1 两面分开）
    // A191——去 `note` 兼容订面（Note 状态单源 = state 段，契约 §12.2 ①；note 事件已退役）
    chatSse = new EventSource('/api/v1/stream?topics=view');
    chatSse.addEventListener('view', function (ev) {
        chatOnFrame(chatParseFrame(ev.data));
    });
    chatSse.onopen = function () {
        // 连接建立（首连 / 自动重连同一路径）：服务端随首帧推全量——前端零请求、零补课
        if (typeof chatPetSetOffline === 'function') { chatPetSetOffline(false); }
    };
    chatSse.onerror = function () {
        warn('SSE 断线——自动重连中');
        if (typeof chatPetSetOffline === 'function') { chatPetSetOffline(true); }
    };
}

/// 启动——引导层在 DOM 就绪后调用（脚本置于页面尾部，DOM 已可用）
function chatBoot() {
    chatConnect();
}
