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

/// 失败可见——控制台出声 + 头部提示（不静默吞；AGENTS 底线四）
function warn(msg, err) {
    if (typeof console !== 'undefined' && console.warn) {
        console.warn('[chat] ' + msg, err || '');
    }
    var info = document.getElementById('chatInfo');
    if (info) {
        info.textContent = '⚠ ' + msg;
    }
}

/// 按钮可用性小件——元素缺失即跳过（防御式）
function setDisabled(id, disabled) {
    var node = document.getElementById(id);
    if (node) {
        node.disabled = (disabled === true);
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
    chatSse = new EventSource('/api/v1/stream?topics=view,note,cmd');
    chatSse.addEventListener('view', function (ev) {
        chatOnFrame(chatParseFrame(ev.data));
    });
    chatSse.addEventListener('note', function (ev) {
        // 兼容面——状态段已含 Note（契约 §12.2 ①）；本事件仅作兜底
        var j = chatParseFrame(ev.data);
        if (j) {
            stateApplyNote(j);
        }
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
