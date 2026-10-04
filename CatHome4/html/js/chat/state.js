// ═══════════════════════════════════════════
// chat/state.js —— 状态段投影（契约 §12.2 ①）
//
// 定位：state 段的唯一应用入口。state = **后端权威业务态整段**（每帧整段覆盖，非增量字段），
//       前端零推断：状态位与数字就位即渲染，不自行计时、不自行推算。
//
// 字段面（后端 ChatSession.BuildStateJson）：
//   { sessionId, runState, runMs{idle,wait,link,think,tool,run,reply}, requests, note{…},
//     delay{entries[…]}, conn{server,clients},
//     tokens{prompt, completion, cacheHit, context, count, sessionPrompt, sessionCompletion, sessionCacheHit} }
//
// 观察项：按钮可用性原列在契约 §12.2 ①（状态段字段面）——按设计口径属**外观层派生**（A184 改写契约），
//         派生处 = fx/controls（独立功能面，A176 归位）；本层只做整段分发 + 状态条 / 头部数字投影。
//         delay 段（A185）为定时面板数据源——原 GET /api/v1/delay 旁路端点退役，面板退化为纯显示。
//         conn 段（A186）为**连接健康**（服务端视角：重启停机中 / 多页面连接数）——它补不了断线可见性
//         （断线后收不到帧），断线态仍由本地 SSE 信号派生（§12.8 pet 行）；重连自愈 = 全量首帧。
// ═══════════════════════════════════════════

/// 当前状态——state 段整段投影（唯一状态源的前端副本）
var appState = {
    sessionId: '',
    runState: '',
    runMs: {},
    requests: 0,
    note: null,
    delay: null,
    conn: null,
    tokens: {}
};

/// 六态元信息——单点声明在 registry.js（A179 收口：状态条与轮末统计共用一份，后端 runMs 键名对齐）

/// 状态段应用——整段覆盖后重绘各消费面（状态条 / 头部数字 / 按钮态 / Note / 延迟面板 / 连接标 / 桌宠）
function stateApply(st) {
    if (!st) {
        return;
    }
    appState = {
        sessionId: st.sessionId || '',
        runState: st.runState || '',
        runMs: st.runMs || {},
        requests: st.requests || 0,
        note: st.note || null,
        delay: st.delay || null,
        conn: st.conn || null,
        tokens: st.tokens || {}
    };
    stateRenderStatus();
    stateRenderTokens();
    stateApplyControls();
    stateRenderNote();
    stateRenderDelay();
    stateRenderConn();
    stateRenderPet();
}

/// 连接健康投影——状态条尾部单标（服务端重启中 / 多页面连接；皆无 → 零标记）。
/// 幂等：先移除旧标再追加——stateRenderStatus 重建时旧标已被清，此处兜底防累积。
function stateRenderConn() {
    var bar = document.getElementById('chatStatus');
    if (!bar) {
        return;
    }
    var old = document.getElementById('chatConn');
    if (old && old.parentNode) {
        old.parentNode.removeChild(old);
    }
    var c = appState.conn;
    if (!c) {
        return;
    }
    var txt = '';
    if (c.server === 'stopping') {
        txt = '🔄 重启中';
    } else if (c.clients > 1) {
        txt = '👥 ' + c.clients;
    }
    if (txt.length === 0) {
        return;
    }
    var span = document.createElement('span');
    span.className = 'st conn';
    span.id = 'chatConn';
    span.textContent = txt;
    bar.appendChild(span);
}

/// 延迟面板投影——state 段 delay 段整段交给面板件（数据源入段后退化为纯显示；件缺失时零动作）
function stateRenderDelay() {
    if (typeof delayApplyState === 'function') {
        delayApplyState(appState.delay);
    }
}


/// 按钮态投影——动作按钮可用性归 fx/controls（独立功能面：外观层派生）；件缺失时零动作
function stateApplyControls() {
    if (typeof fxControlsApply === 'function') {
        fxControlsApply(appState);
    }
}

/// 状态条——六态完成后端时长（当前态高亮）+ ⏱ 总 + 请求次数；无数据整行空
function stateRenderStatus() {
    var bar = document.getElementById('chatStatus');
    if (!bar) {
        return;
    }
    var ms = appState.runMs || {};
    var active = appState.runState || '';
    var html = '';
    var total = 0;
    for (var i = 0; i < RUN_PHASES.length; i++) {
        var p = RUN_PHASES[i];
        var v = ms[p.key] || 0;
        total = total + v;
        if (v <= 0 && active !== p.key) {
            continue;
        }
        var cls = (active === p.key) ? ' active' : '';
        html += '<span class="st ' + p.key + cls + '">' + p.icon + ' ' + p.label + ' ' + fmtMs(v) + '</span>';
    }
    if (total > 0) {
        html += '<span class="st total">⏱ All ' + fmtMs(total) + '</span>';
    }
    if (appState.requests > 0) {
        var api = '🔄 Api ' + appState.requests;
        if ((ms.link || 0) > 0) {
            api += '（' + (ms.link / 1000 / appState.requests).toFixed(2) + ' s /use）';
        }
        html += '<span class="st req">' + api + '</span>';
    }
    bar.innerHTML = html;
}

/// 头部数字——前文条数 / sessionId / 前文长度（请求级最新值）；经信息位单点写（chatInfoSet——state 段专属）
function stateRenderTokens() {
    var t = appState.tokens || {};
    var txt = '前文 ' + fmtCount(t.count || 0) + ' 条 | sessionId=' + appState.sessionId;
    if (t.context > 0) {
        txt += ' | 前文 ' + fmtCount(t.context) + ' tokens';
    }
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(txt);
    }
}

/// Note 投影——待 Note 面板件接入（本轮留钩子，无容器时零动作）
function stateRenderNote() {
    if (typeof noteRenderFromState === 'function') {
        noteRenderFromState(appState.note);
    }
}

/// 桌宠投影——状态段渲染后同步桌宠（纯前端调度，零后端面；无容器时零动作）
function stateRenderPet() {
    if (typeof chatPetSync === 'function') {
        chatPetSync();
    }
}
