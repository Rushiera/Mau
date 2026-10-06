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

/// 顶部信息位（#chatInfo）唯一写入口——**state 段专属**（后端必要信息：前文条数 / 前文长度 / 会话消耗）
/// 归位（A179 收尾）：前端告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本处不再有竞写
/// 富文本载荷（2026-10-06）：内容由投影件组装为分片（数值 `.ci-num` 高亮 / 命中率 `.ci-rate` 淡紫）——
///   片段全部为固定字面量 + 数值，**无用户文本**，故直投 innerHTML（转义面为空）
function chatInfoSet(html) {
    var info = document.getElementById('chatInfo');
    if (info) {
        info.innerHTML = html;
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
/// 贴近底部判定——距底 < 80px 视为「用户在看最新内容」（用户上翻读历史时不打扰）
/// 🔴 采样时机 = **内容追加之前**（2026-10-06 判例：追加后采样会被新块自身高度顶出阈值 → 永不跟随）
function scrollWanted() {
    var box = persistContainer();
    if (!box) {
        return false;
    }
    return box.scrollHeight - box.scrollTop - box.clientHeight < 80;
}

/// 跟随循环序号——**单例守卫**：新请求递增序号并接管，旧循环下一帧自行退出
/// （多循环并存会互相把对方的设值判成「用户滚动」→ 集体退出 = 滚不到底；2026-10-06 判例）
var scrollFollowSeq = 0;

/// 平滑跟随——追加后调用：视口向内容底部**渐变推移**（每帧走剩余距离约 1/4，非跳转），
/// 内容稳定且已在底部即收工；用户主动滚动即让位（交还控制权）
function scrollFollow() {
    var box = persistContainer();
    if (!box) {
        return;
    }
    if (typeof requestAnimationFrame !== 'function') {
        box.scrollTop = box.scrollHeight;
        return;
    }
    scrollFollowSeq = scrollFollowSeq + 1;
    var seq = scrollFollowSeq;
    var lastSet = box.scrollTop;
    var idle = 0;
    var step = function () {
        if (seq !== scrollFollowSeq) {
            return;
        }
        // 让位判定（同步值比对）——我方是唯一设值方，现值与上帧设定值不符 = 用户滚动/拖动 → 交还控制权
        // （不用 scroll 事件：它异步派发，到达时我方已更新期望值，判据恒看不出用户动作——2026-10-06 判例）
        if (Math.abs(box.scrollTop - lastSet) > 2) {
            return;
        }
        var target = box.scrollHeight - box.clientHeight;
        if (box.scrollTop >= target - 0.5) {
            // 已到底——连续约 0.2s 无新增即收工（内容继续增长会复位 idle，自动接着跟随）
            idle = idle + 1;
            if (idle > 12) {
                return;
            }
        }
        else {
            idle = 0;
            // 渐变逼近——每帧走剩余距离的一部分（至少 1px）：观感为平滑推移，非跳转
            var next = box.scrollTop + Math.max(1, (target - box.scrollTop) / 4);
            box.scrollTop = next > target ? target : next;
        }
        lastSet = box.scrollTop;
        requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
}

/// 滚底——无条件瞬时贴底（全量重绘 / 新会话 / 快捷跳底按钮）
function scrollBottomNow() {
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
        // A206——断线即收临时区：不让半句残影 + 进行中光标留到重连（持久区由重连全量首帧重建）
        if (typeof chatLiveAbort === 'function') { chatLiveAbort(); }
    };
}

/// 启动——引导层在 DOM 就绪后调用（脚本置于页面尾部，DOM 已可用）
function chatBoot() {
    chatConnect();
}
