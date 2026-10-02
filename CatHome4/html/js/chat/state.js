// ═══════════════════════════════════════════
// chat/state.js —— 状态段投影（契约 §12.2 ①）
//
// 定位：state 段的唯一应用入口。state = **后端权威业务态整段**（每帧整段覆盖，非增量字段），
//       前端零推断：状态位与数字就位即渲染，不自行计时、不自行推算。
//
// 字段面（后端 ChatSession.BuildStateJson）：
//   { sessionId, runState, runMs{idle,wait,link,think,tool,run,reply}, requests, note{…},
//     tokens{prompt, completion, cacheHit, context, count, sessionPrompt, sessionCompletion, sessionCacheHit} }
//
// 观察项：按钮可用性未在 state 段显式给（契约 §12.2 ① 列为状态段内容）——本层按 runState 派生，
//         待后端补字段后改为直取（记 A167 观察项，不自行扩协议面）。
// ═══════════════════════════════════════════

/// 当前状态——state 段整段投影（唯一状态源的前端副本）
var appState = {
    sessionId: '',
    runState: '',
    runMs: {},
    requests: 0,
    note: null,
    tokens: {}
};

/// 六态元信息——状态条渲染顺序与图标（后端 runMs 键名对齐）
var RUN_PHASES = [
    { key: 'link', label: 'Link', icon: '🔗' },
    { key: 'wait', label: 'Wait', icon: '⏳' },
    { key: 'think', label: 'Think', icon: '🧠' },
    { key: 'tool', label: 'Tool', icon: '🔧' },
    { key: 'run', label: 'Run', icon: '⚙️' },
    { key: 'reply', label: 'Reply', icon: '💬' }
];

/// 状态段应用——整段覆盖后重绘三个消费面（状态条 / 头部数字 / 按钮可用性）
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
        tokens: st.tokens || {}
    };
    stateRenderStatus();
    stateRenderTokens();
    stateSyncControls();
    stateRenderNote();
}

/// Note 段单独应用——note 事件为兼容面（状态段已含 Note）；载荷形态与 state.note 一致
function stateApplyNote(note) {
    appState.note = note || null;
    stateRenderNote();
}

/// 轮进行中判据——runState 非空且非 idle（后端权威态，前端不猜）
function stateIsRunning() {
    var s = appState.runState || '';
    return s !== '' && s !== 'idle';
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

/// 头部数字——前文条数 / sessionId / 前文长度（请求级最新值）
function stateRenderTokens() {
    var info = document.getElementById('chatInfo');
    if (!info) {
        return;
    }
    var t = appState.tokens || {};
    var txt = '前文 ' + fmtCount(t.count || 0) + ' 条 | sessionId=' + appState.sessionId;
    if (t.context > 0) {
        txt += ' | 前文 ' + fmtCount(t.context) + ' tokens';
    }
    info.textContent = txt;
}

/// 按钮可用性——按轮进行态派生（发送恒可用；忙时插话走队列语义）
function stateSyncControls() {
    var running = stateIsRunning();
    setDisabled('chatPause', !running);
    setDisabled('chatContinue', running);
    setDisabled('noteStartBtn', running);
    setDisabled('chatSendBtn', false);
    setDisabled('chatSendInput', false);
}

/// Note 投影——待 Note 面板件接入（本轮留钩子，无容器时零动作）
function stateRenderNote() {
    if (typeof noteRenderFromState === 'function') {
        noteRenderFromState(appState.note);
    }
}
