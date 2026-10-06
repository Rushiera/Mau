// ═══════════════════════════════════════════
// chat/stream.js —— 流式呈现机制（前缀增量 + 打字机 + 面板跟随 + 段期光标）
//
// 定位：**「整套流式效果」的唯一承载处**（2026-10-06 自 live.js 独立出来 · 莎定）——任何 live 件
//   （临时区按 type 分发的流式渲染件）挂上本件即获得同一套呈现：前缀增量、打字机吐字、
//   容器贴底跟随、段期进行中光标。挂载面 = `STREAM_TYPES` 白名单（当前 `thinksse` + `replysse`）。
//
// 输入面不变（契约 §12.5）：调用方每次给**该流当前全文**（全集）——本件不做协议层合并，
//   只做呈现层增量：拿新全集与「已显 + 待吐」比前缀，只把新增的尾巴入队。
//
// 写入点约定：被挂载的 live 件产出的**块体内首个文本节点**即打字机写入点——渲染件以空文本
//   成形即可（无文本节点时本件补建；见 `blocks/think-sse.js` / `blocks/reply-sse.js`）。
//
// 打字机节拍（契约 §12.5 呈现口径）：
//   **标称 60 步/秒 · 按墙钟走步**（与显示器刷新率解耦）——每步吐字 = ceil(剩余积压 ÷ 剩余步数)，
//   夹逼 [每步下限, `STREAM_FRAME_MAX`]；每批增量在窗口 `STREAM_DRAIN_MS` 内追平（后端 250ms 一推）。
//
// 统计出口（`streamStats`）：把段内原始事实（类型 / 字符数 / 行数 / 段用时）交给显示件——
//   本件只报事实、不算显示口径（口径归 `fx/live-stats.js` · 契约 §12.8 独立功能面）。
//
// 状态：单实例（临时区一次只有一条流）——`streamState` 持容器 / 行 / 块体 / 写入点 / 已显 / 待吐 / 走步。
// ═══════════════════════════════════════════

/// 流式件白名单——**挂上整套流式效果**的 live type；未列入者由调用方走整体替换（状态投影原语义）。
/// 挂载前提：该 type 在 `LIVE_RENDERERS` 有渲染件（缺件即回落整体替换——不静默空面板）
var STREAM_TYPES = { 'thinksse': true, 'replysse': true };

/// 标称帧率（步/秒）——走步步长（1000 ÷ FPS 毫秒）与「窗口内剩余步数」均按此折算
var STREAM_FPS = 60;

/// 打字机窗口——本次增量摊到下一次刷新之前（后端 250ms 一推，留 30ms 余量；60fps 下约 13 步）
var STREAM_DRAIN_MS = 220;

/// 走步容差（单位 = 标称步）——帧间隔与标称步长不同源（120Hz 帧 8.33ms vs 步长 16.67ms）时，
/// 取整损失会让走步频率掉档；容差吸收帧间抖动与取整误差（0.35 步 ≈ 5.8ms）
var STREAM_STEP_TOL = 0.35;

/// 单步下限——每步至少吐 1 字（下限即最小吐字速度：60 步/秒 ≈ 60 字/秒）
var STREAM_MIN_CHARS = 1;

/// 单步上限——重试 / 续传级大段 dump 也保持推进感，不瞬间灌满
var STREAM_FRAME_MAX = 20;

/// 单次补步上限——掉帧 / 切后台回来时一次最多补的步数（≈133ms），避免一次性灌满
var STREAM_CATCHUP_STEPS = 8;

/// 流式呈现状态（临时区一次只有一条流）：
///   box——容器（跟随面）；row / body / node——行 / 块体 / 写入点；
///   revealed——已吐到屏幕的文本（前缀比对基准）；queue——待吐尾巴；
///   dueAt——本批增量的追平时刻；stepBase / stepsDone——走步起点与已走步数（每批重起）；
///   startedAt——**本段起点**（统计用：段用时 = now − startedAt）；newlines——段内换行数（统计用）
var streamState = {
    box: null,
    type: '',
    row: null,
    body: null,
    node: null,
    revealed: '',
    queue: '',
    dueAt: 0,
    stepBase: 0,
    stepsDone: 0,
    startedAt: 0,
    newlines: 0,
    raf: 0
};

/// 时间源——走步只认单调毫秒（测试可直喂时刻）
function streamNow() {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') {
        return performance.now();
    }
    return Date.now();
}

/// 步长——标称帧率的倒数（毫秒）；走步与「窗口内剩余步数」均按此折算
function streamFrameMs() {
    return 1000 / STREAM_FPS;
}

/// 换行计数——统计用（每次收帧重算一遍：字符数与行数同源，批级成本可忽略）
function streamCountBreaks(text) {
    var n = 0;
    var i = text.indexOf('\n');
    while (i >= 0) {
        n = n + 1;
        i = text.indexOf('\n', i + 1);
    }
    return n;
}

/// 挂载判定——白名单 + 渲染件齐备（缺渲染件回落整体替换路径，不静默空面板）
function streamWanted(type) {
    return STREAM_TYPES[type] === true && typeof LIVE_RENDERERS[type] === 'function';
}

/// 流式段入口——前缀三态：
///   ① 前缀命中（新全集以「已显 + 待吐」开头）→ **续接**：只把新增尾巴入队
///   ② 已显前缀命中、尾巴变短 → **重排**：后端改写 / 回缩尾巴，按已显前缀重排队列（已吐字不回收）
///   ③ 全不命中 → **重置**：新一轮 / 新会话——清区重建，全文从头吐
function streamApply(box, type, text) {
    var fresh = streamEnsure(box, type);
    if (fresh) {
        streamState.queue = text;
    } else {
        var known = streamState.revealed + streamState.queue;
        if (text.length >= known.length && text.slice(0, known.length) === known) {
            streamState.queue = streamState.queue + text.slice(known.length);
        } else if (text.length >= streamState.revealed.length && text.slice(0, streamState.revealed.length) === streamState.revealed) {
            streamState.queue = text.slice(streamState.revealed.length);
        } else {
            streamRebuild(box, type);
            streamState.queue = text;
        }
    }
    streamState.newlines = streamCountBreaks(streamState.revealed) + streamCountBreaks(streamState.queue);
    // 走步窗口每批重起——批间剩余步数不互相污染（否则攒下的「剩余步数」会变成突发）
    streamState.stepBase = streamNow();
    streamState.stepsDone = 0;
    streamState.dueAt = streamState.stepBase + STREAM_DRAIN_MS;
    streamCursor(true);
    streamSchedule();
    streamNotifyStats();
}

/// 流式行就位——容器 / 行 / 块体 / 写入点四件；同 type 且行仍在原容器即复用（返回 false = 复用）
function streamEnsure(box, type) {
    if (streamState.type === type && streamState.box === box && streamState.row && streamState.row.parentNode === box && streamState.node) {
        return false;
    }
    streamRebuild(box, type);
    return true;
}

/// 清区重建——按 type 新建行与写入点（状态归零）；渲染件以空文本成形，正文由走步逐帧写入；
/// **本段起点在此落定**（段用时口径 = 从本段第一次成形起算）
function streamRebuild(box, type) {
    streamReset();
    box.textContent = '';
    var row = LIVE_RENDERERS[type]({ text: '' });
    box.appendChild(row);
    var body = row.firstElementChild;
    var node = body ? body.firstChild : null;
    if (!node || node.nodeType !== 3) {
        node = document.createTextNode('');
        if (body) {
            body.appendChild(node);
        }
    }
    streamState.box = box;
    streamState.type = type;
    streamState.row = row;
    streamState.body = body;
    streamState.node = node;
    streamState.startedAt = streamNow();
}

/// 停泵与状态归零（DOM 由调用方处置——整体替换路径自己清区；光标随段开合，离段即摘）
function streamReset() {
    streamCursor(false);
    if (streamState.raf && typeof cancelAnimationFrame === 'function') {
        cancelAnimationFrame(streamState.raf);
    }
    streamState.raf = 0;
    streamState.box = null;
    streamState.type = '';
    streamState.row = null;
    streamState.body = null;
    streamState.node = null;
    streamState.revealed = '';
    streamState.queue = '';
    streamState.dueAt = 0;
    streamState.stepBase = 0;
    streamState.stepsDone = 0;
    streamState.startedAt = 0;
    streamState.newlines = 0;
    streamNotifyStats();
}

/// 走步排程——有积压才排帧；宿主无帧回调（单测 / 老浏览器）即当场铺完（诚实降级，不留半屏）
function streamSchedule() {
    if (streamState.queue.length === 0) {
        return;
    }
    if (typeof requestAnimationFrame !== 'function') {
        streamDrain();
        return;
    }
    if (!streamState.raf) {
        streamState.raf = requestAnimationFrame(streamTick);
    }
}

/// 帧回调——推一步；仍有积压续排
function streamTick() {
    streamState.raf = 0;
    if (streamPump(streamNow())) {
        streamSchedule();
    }
}

/// 走步推进——**按标称步走**（与刷新率无关）：自本批起点起「应走的步数 − 已走步数」即本次要吐的步数；
///   未满一步则本帧不吐字（高刷屏多余帧被吸收）；每步吐字 = ceil(剩余积压 ÷ 剩余步数)，
///   夹逼 [`STREAM_MIN_CHARS`, `STREAM_FRAME_MAX`]。
/// 返回是否仍有积压。🔴 跟随判据在**写入之前**采样——写入后再采会被自身增高顶出阈值（2026-10-06 主区同源判例）
function streamPump(nowMs) {
    if (!streamState.node) {
        return false;
    }
    if (streamState.queue.length === 0) {
        return false;
    }
    var stepMs = streamFrameMs();
    var due = Math.floor((nowMs - streamState.stepBase + stepMs * STREAM_STEP_TOL) / stepMs);
    if (due < 0) {
        due = 0;
    }
    var steps = due - streamState.stepsDone;
    streamState.stepsDone = due;
    if (steps < 1) {
        return true;
    }
    if (steps > STREAM_CATCHUP_STEPS) {
        steps = STREAM_CATCHUP_STEPS;
    }
    var stepLeft = Math.ceil((streamState.dueAt - nowMs) / stepMs);
    if (stepLeft < 1) {
        stepLeft = 1;
    }
    var take = Math.ceil(streamState.queue.length / stepLeft);
    if (take < STREAM_MIN_CHARS) {
        take = STREAM_MIN_CHARS;
    }
    if (take > STREAM_FRAME_MAX) {
        take = STREAM_FRAME_MAX;
    }
    take = take * steps;
    if (take > streamState.queue.length) {
        take = streamState.queue.length;
    }
    var want = streamFollowWanted();
    streamState.revealed = streamState.revealed + streamState.queue.slice(0, take);
    streamState.queue = streamState.queue.slice(take);
    streamState.node.data = streamState.revealed;
    if (want) {
        scrollBoxBottom(streamState.box);
    }
    streamNotifyStats();
    return streamState.queue.length > 0;
}

/// 一次性铺完——无帧回调宿主与测试驱动的收口通道（按到期之后逐步推至吐完）
function streamDrain() {
    var i = 0;
    while (streamState.queue.length > 0 && i < 5000) {
        streamPump(streamState.dueAt + (i + 1) * streamFrameMs());
        i = i + 1;
    }
}

/// 统计出口——把段内**原始事实**交给显示件：类型 / 字符数（已显 + 待吐 = 已收到全文）/ 行数 / 段用时；
///   返回 null = 当前无活跃流式段（显示件据此隐藏）。显示口径（速度取整 / 一位小数）不在此处
function streamStats() {
    if (!streamState.type) {
        return null;
    }
    var chars = streamState.revealed.length + streamState.queue.length;
    var lines = 0;
    if (chars > 0) {
        lines = streamState.newlines + 1;
    }
    var elapsed = 0;
    if (streamState.startedAt > 0) {
        elapsed = streamNow() - streamState.startedAt;
    }
    if (elapsed < 0) {
        elapsed = 0;
    }
    return { type: streamState.type, chars: chars, lines: lines, elapsedMs: elapsed };
}

/// 统计刷新通知——显示件（`fx/live-stats.js`）在场才调（防御式可选钩子：机制不依赖显示件）
function streamNotifyStats() {
    if (typeof liveStatsRefresh === 'function') {
        liveStatsRefresh();
    }
}

/// 进行中光标——块体挂 `streaming`（CSS `.chat-plain.streaming::after` 单点出 ▌ 闪烁）。
/// 开合由**流式段生命周期**驱动（进段挂 / 离段摘）——不随走步停摆：段间空档（两次推送之间）仍是
/// 「思考中」，光标闪断会给「已结束」的错觉（2026-10-06 真机实测所见 30ms 闪断，改按段）
function streamCursor(on) {
    if (!streamState.body || !streamState.body.classList) {
        return;
    }
    if (on) {
        streamState.body.classList.add('streaming');
    } else {
        streamState.body.classList.remove('streaming');
    }
}

/// 跟随判据——容器贴近底部（<24px）视为「看的就是最新」；用户上翻即让位（读早期内容不被打断）
function streamFollowWanted() {
    var box = streamState.box;
    if (!box) {
        return false;
    }
    return (box.scrollHeight - box.scrollTop - box.clientHeight) < 24;
}
