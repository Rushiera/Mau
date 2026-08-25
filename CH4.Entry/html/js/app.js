// CH4 外观层 v2——app.js：全局状态 + SSE 连接 + 快照/patch + 状态区 + 日志区 + 指令 + 配置区（2026-08-25 拆分自 index.html）
// 加载顺序：app.js 最先（全局变量与公共函数在此定义；chat.js/panel.js 依赖本文件）

// [段1] 全局状态与 DOM 引用
var metaEl = document.getElementById('meta');
var catsEl = document.getElementById('cats');
var oaEl = document.getElementById('oa');
var boxesBody = document.querySelector('#boxes tbody');
var logEvents = document.getElementById('logevents');
var logFilterEl = document.getElementById('logFilter');
var rawJsonEl = document.getElementById('rawJson');
var rawInfoEl = document.getElementById('rawInfo');
var rawPauseBtn = document.getElementById('rawPause');
var cmdResultEl = document.getElementById('cmdResult');
var chatMsgs = document.getElementById('chatMsgs');
var chatInput = document.getElementById('chatSendInput');
var chatBtn = document.getElementById('chatSendBtn');
var chatInfo = document.getElementById('chatInfo');
var chatState = 'loading';
var chatStage = null;
var chatToolQueue = [];
var chatTimer = null;
var rawPaused = false;
var lastFrame = -1;          // 快照帧号守卫——旧快照不覆盖新快照
var fullSnapshot = null;     // 本地全快照——增量流式接收层维护（各页面渲染唯一数据源）
var logs = [];               // 内存日志数组（Log 区真相源）
var logDedupeKey = '';       // 已渲染最后一条日志的 key——拉取合并去重

// [段2] 侧栏页签切换
var tabs = document.querySelectorAll('#sidebar button');
for (var i = 0; i < tabs.length; i++) {
    tabs[i].addEventListener('click', function () {
        for (var j = 0; j < tabs.length; j++) { tabs[j].classList.remove('active'); }
        this.classList.add('active');
        var target = this.getAttribute('data-tab');
        var sections = document.querySelectorAll('.tab');
        for (var k = 0; k < sections.length; k++) { sections[k].classList.remove('active'); }
        document.getElementById('tab-' + target).classList.add('active');
    });
}

// [段3] SSE 事件流——四类事件分派
var es = new EventSource('/api/v1/stream');
es.addEventListener('snapshot', function (ev) {
    // 增量流式——全量快照到达（helloFrame 重连兜底）整体替换本地全快照
    var s = JSON.parse(ev.data);
    fullSnapshot = s;
    applySnapshot(s);
});
es.addEventListener('patch', function (ev) { applyPatch(JSON.parse(ev.data)); });   // 增量流式——变化段合并进本地全快照
es.addEventListener('log', function (ev) { pushLog(JSON.parse(ev.data)); });
es.addEventListener('llm', function (ev) {
    var d = JSON.parse(ev.data);
    var kindText = d.kind === 'text' ? 'T' : (d.kind === 'reasoning' ? 'R' : d.kind);
    pushLog({ time: '', frame: 0, level: 'INFO', category: 'LLM', module: 'llm#' + d.seq, message: '[' + kindText + '] ' + d.text });
    chatOnLlm(d);   // B4 对话区——sessionId 过滤 + 五态渲染
});
es.addEventListener('cmd', function (ev) {
    var d = JSON.parse(ev.data);
    pushLog({ time: '', frame: d.frame, level: d.ok ? 'INFO' : 'ERROR', category: 'CMD', module: d.cmdId, message: d.ok ? 'ok' : ('err=' + (d.error || '')) });
    cmdResultEl.textContent = '回执: ' + d.cmdId + ' ok=' + d.ok + ' frame=' + d.frame + (d.error ? ' error=' + d.error : '');
});
es.addEventListener('tool', function (ev) { chatOnTool(JSON.parse(ev.data)); });   // B4 对话区——工具结果实时回填
es.addEventListener('chatdone', function (ev) { chatOnChatDone(JSON.parse(ev.data)); });   // B4——会话终态（llm done 仅一轮；chatdone 才定型）
es.addEventListener('note', function (ev) { noteOnEvent(JSON.parse(ev.data)); });   // M4c——Note 状态实时重绘
es.addEventListener('user', function (ev) { chatOnUser(JSON.parse(ev.data)); });   // 单向数据流——内核确认消息出口
es.onerror = function () { metaEl.textContent = 'SSE 断线——自动重连...'; };
es.onopen = function () {
    // 重连成功兜底——无条件重拉历史（CHAT_SESSION 动态更新 + 历史重绘；sending 态额外清阶段）
    // 判例：页面加载时 history fetch 失败（宿主未就绪）→ CHAT_SESSION 卡 'majordomo' → llm 事件全被 sessionId 过滤 → 对话气泡不渲染
    if (chatState === 'sending') {
        chatStage = null;
        chatToolQueue = [];
        if (chatTimer) { clearTimeout(chatTimer); chatTimer = null; }
    }
    chatLoadHistory();
};

// 初始兜底——帧号守卫（旧快照不覆盖新）
fetch('/api/v1/snapshot?logs=200')
    .then(function (r) { return r.json(); })
    .then(function (s) {
        for (var i = 0; i < s.logs.length; i++) { unshiftLog(s.logs[i]); }
        if (s.frame >= lastFrame) { fullSnapshot = s; applySnapshot(s); lastFrame = s.frame; }
        refreshLogView();
    })
    .catch(function () {});

// [段3b] patch 合并——变化段合并进本地全快照（增量流式：接收层处理，页面渲染逻辑不变）
function applyPatch(p) {
    if (!fullSnapshot) { return; }                    // 全量未到——忽略（helloFrame 会来）
    if (p.frame <= fullSnapshot.frame) { return; }    // 过期帧忽略
    if (p.cats) { fullSnapshot.cats = p.cats; }
    if (p.oa) { fullSnapshot.oa = p.oa; }
    if (p.boxes) {
        var map = {};
        var cur = fullSnapshot.boxes || [];
        for (var i = 0; i < cur.length; i++) {
            map[cur[i].scope + '\u0001' + cur[i].key] = cur[i];
        }
        var set = p.boxes.set || [];
        for (var j = 0; j < set.length; j++) {
            map[set[j].scope + '\u0001' + set[j].key] = set[j];
        }
        var del = p.boxes.del || [];
        for (var k = 0; k < del.length; k++) {
            delete map[del[k].scope + '\u0001' + del[k].key];
        }
        var arr = [];
        for (var mk in map) {
            if (Object.prototype.hasOwnProperty.call(map, mk)) { arr.push(map[mk]); }
        }
        fullSnapshot.boxes = arr;
    }
    fullSnapshot.frame = p.frame;
    applySnapshot(fullSnapshot);
}

// [段4] 快照应用——状态区 + 原始区（同源渲染）
function applySnapshot(s) {
    if (s.frame < lastFrame) { return; }   // 帧号守卫
    lastFrame = s.frame;
    metaEl.textContent = 'pid=' + s.pid + ' frame=' + s.frame + ' version=' + s.version;
    renderCats(s.cats);
    oaEl.textContent = 'Open=' + s.oa.open + ' Work=' + s.oa.work + ' Closed=' + s.oa.closed + ' Timeout=' + s.oa.timeout;
    renderBoxes(s.boxes || []);
    if (!rawPaused) {
        rawJsonEl.textContent = JSON.stringify(s, null, 2);
        rawInfoEl.textContent = 'frame=' + s.frame + ' · ' + s.cats.length + ' cats · ' + (s.boxes ? s.boxes.length : 0) + ' boxes';
    }
}

// [段5] Cat 卡片渲染——DOM API + textContent（安全铁律）；全字段消费：stateLines/sensors/slots/wires
function renderCats(cats) {
    catsEl.textContent = '';
    for (var i = 0; i < cats.length; i++) {
        var c = cats[i];
        var card = document.createElement('div');
        card.className = 'cat-card';
        var head = document.createElement('div');
        head.className = 'cat-head';
        var name = document.createElement('span');
        name.className = 'cat-name';
        name.textContent = c.name;
        var id = document.createElement('span');
        id.className = 'cat-id';
        id.textContent = '#' + c.id;
        head.appendChild(name);
        head.appendChild(id);
        card.appendChild(head);
        if (c.faulted) {
            var fault = document.createElement('div');
            fault.className = 'cat-faulted';
            fault.textContent = 'FAULTED: ' + c.faultReason;
            card.appendChild(fault);
        } else {
            var st = c.status || {};
            // 状态机行
            var states = document.createElement('div');
            states.className = 'cat-states';
            states.textContent = (st.stateLines || []).join('  ');
            card.appendChild(states);
            // 传感器行——信号真相（bool 着色）
            var sens = (st.sensors || []).map(function (x) {
                var span = document.createElement('span');
                span.className = (x.value === true || x.value === 'true') ? 'sen-on' : 'sen-off';
                span.textContent = (x.value === true || x.value === 'true') ? '● ' : '○ ';
                span.appendChild(document.createTextNode(x.name + '=' + String(x.value)));
                return span;
            });
            appendRow(card, 'sensors', sens);
            // 槽位行
            appendRow(card, 'slots', (st.slots || []).map(function (x) { return x.name + ' ' + x.available + '/' + x.capacity; }));
            // 导线行——busy/timedOut/lastTriggerFrame 全展示
            var wires = (st.wires || []).map(function (w) {
                var span = document.createElement('span');
                span.className = w.timedOut ? 'wire-timeout' : (w.busy ? 'wire-busy' : '');
                span.textContent = w.name + (w.busy ? '(busy)' : '') + (w.timedOut ? '(timeout)' : '') + '@' + w.lastTriggerFrame;
                return span;
            });
            appendRow(card, 'wires', wires);
        }
        catsEl.appendChild(card);
    }
}
function appendRow(card, label, spans) {
    if (spans.length === 0) { return; }
    var row = document.createElement('div');
    row.className = 'cat-row';
    var k = document.createElement('span');
    k.className = 'k';
    k.textContent = label + ': ';
    row.appendChild(k);
    for (var i = 0; i < spans.length; i++) {
        if (i > 0) { row.appendChild(document.createTextNode(' · ')); }
        row.appendChild(spans[i]);
    }
    card.appendChild(row);
}

// [段6] 盒子渲染——textContent（值可能来自文件内容——XSS 禁地）
function renderBoxes(boxes) {
    boxesBody.textContent = '';
    for (var i = 0; i < boxes.length; i++) {
        var b = boxes[i];
        var tr = document.createElement('tr');
        var td1 = document.createElement('td');
        td1.className = 'box-key';
        td1.textContent = b.scope + '.' + b.key;
        var td2 = document.createElement('td');
        td2.className = 'box-tag';
        td2.textContent = b.t;
        var td3 = document.createElement('td');
        td3.className = 'box-val';
        td3.textContent = String(b.value);
        tr.appendChild(td1); tr.appendChild(td2); tr.appendChild(td3);
        boxesBody.appendChild(tr);
    }
}

// [段7] Log 流——内存数组 + 筛选 + 去重
function pushLog(e) {
    logs.push({ time: e.time, frame: e.frame, level: e.level, category: e.category, module: e.module, message: e.message });
    if (logs.length > 3000) { logs.shift(); }
    appendLogDom(logs[logs.length - 1]);
}
function unshiftLog(e) {
    // 拉取合并——跳过已渲染过的尾部（时间+帧+消息 去重键）
    var key = e.time + '|' + e.frame + '|' + e.message;
    if (logDedupeKey !== '' && key <= logDedupeKey) { return; }
    logDedupeKey = key;
    logs.push(e);
}
function appendLogDom(e) {
    var div = document.createElement('div');
    div.className = logClass(e);
    div.textContent = logText(e);
    logEvents.appendChild(div);
    while (logEvents.children.length > 2000) { logEvents.removeChild(logEvents.firstChild); }
    logEvents.scrollTop = logEvents.scrollHeight;
    applyFilterToEl(div);
}
function logText(e) {
    var t = e.time ? e.time + ' ' : '';
    return t + (e.frame ? 'F' + e.frame + ' ' : '') + e.level + ' | ' + (e.category || '-') + ' | ' + (e.module || '') + (e.message ? ' ' + e.message : '');
}
function logClass(e) {
    if (e.level === 'ERROR') { return 'lg lg-error'; }
    if (e.level === 'WARN') { return 'lg lg-warn'; }
    if (e.category === 'CMD') { return 'lg lg-cmd'; }
    if (e.category === 'LLM') { return 'lg lg-llm'; }
    return 'lg';
}

// [段8] 筛选——level=X / cat=X / 裸词=关键词（重新渲染）
logFilterEl.addEventListener('input', refreshLogView);
function refreshLogView() {
    logEvents.textContent = '';
    for (var i = 0; i < logs.length; i++) {
        var div = document.createElement('div');
        div.className = logClass(logs[i]);
        div.textContent = logText(logs[i]);
        logEvents.appendChild(div);
    }
    var filter = logFilterEl.value.trim();
    if (filter.length > 0) { applyFilterAll(filter); }
    logEvents.scrollTop = logEvents.scrollHeight;
}
function applyFilterAll(filter) {
    var tokens = filter.split(/\s+/);
    var children = logEvents.children;
    for (var i = 0; i < children.length; i++) {
        var el = children[i];
        var text = el.textContent;
        var pass = true;
        for (var j = 0; j < tokens.length; j++) {
            var t = tokens[j];
            if (t.indexOf('level=') === 0) {
                if (text.indexOf('| ' + t.substring(6).toUpperCase() + ' |') < 0) { pass = false; break; }
            } else if (t.indexOf('cat=') === 0) {
                if (text.indexOf('| ' + t.substring(4).toUpperCase() + ' |') < 0) { pass = false; break; }
            } else {
                if (text.indexOf(t) < 0) { pass = false; break; }
            }
        }
        el.style.display = pass ? '' : 'none';
    }
}
function applyFilterToEl(el) {
    var filter = logFilterEl.value.trim();
    if (filter.length === 0) { return; }
    var tokens = filter.split(/\s+/);
    var text = el.textContent;
    for (var j = 0; j < tokens.length; j++) {
        var t = tokens[j];
        if (t.indexOf('level=') === 0) {
            if (text.indexOf('| ' + t.substring(6).toUpperCase() + ' |') < 0) { el.style.display = 'none'; return; }
        } else if (t.indexOf('cat=') === 0) {
            if (text.indexOf('| ' + t.substring(4).toUpperCase() + ' |') < 0) { el.style.display = 'none'; return; }
        } else {
            if (text.indexOf(t) < 0) { el.style.display = 'none'; return; }
        }
    }
}

// [段9] 拉取 N 条——?logs=N 服务端合成 + 尾部去重合并
document.getElementById('logRefresh').addEventListener('click', function () {
    fetch('/api/v1/snapshot?logs=200')
        .then(function (r) { return r.json(); })
        .then(function (s) {
            logs = [];
            logDedupeKey = '';
            for (var i = 0; i < s.logs.length; i++) { unshiftLog(s.logs[i]); }
            refreshLogView();
        })
        .catch(function () {});
});

// [段10] 原始区暂停/继续
rawPauseBtn.addEventListener('click', function () {
    rawPaused = !rawPaused;
    rawPauseBtn.textContent = rawPaused ? '继续' : '暂停';
    rawPauseBtn.classList.toggle('paused', rawPaused);
    if (!rawPaused) {
        fetch('/api/v1/snapshot')
            .then(function (r) { return r.json(); })
            .then(function (s) { if (s.frame >= lastFrame) { fullSnapshot = s; applySnapshot(s); } })
            .catch(function () {});
    }
});

// [段11] 指令入口——POST /api/v1/command
function sendCmd() {
    var input = document.getElementById('cmdInput');
    var text = input.value.trim();
    if (text.length === 0) { return; }
    input.value = '';
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: text })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            cmdResultEl.textContent = '回执: cmdId=' + d.cmdId + ' ok=' + d.ok + ' frame=' + d.frame + (d.error ? ' error=' + d.error : '');
        })
        .catch(function (err) {
            cmdResultEl.textContent = '请求失败: ' + err;
        });
}

// [段12] 配置区——GET/POST /api/v1/config（v3 配置区；敏感键服务端掩码，POST 拒绝掩码回写）
var configTableBody = document.querySelector('#configTable tbody');
var configMsg = document.getElementById('configMsg');
function loadConfig() {
    fetch('/api/v1/config')
        .then(function (r) { return r.json(); })
        .then(function (d) { renderConfig(d.items || []); })
        .catch(function () {});
}
function renderConfig(items) {
    configTableBody.textContent = '';
    for (var i = 0; i < items.length; i++) {
        var it = items[i];
        var tr = document.createElement('tr');
        var td1 = document.createElement('td');
        td1.className = 'box-key';
        td1.textContent = it.key;
        var td2 = document.createElement('td');
        var inp = document.createElement('input');
        inp.value = it.value;
        inp.style.cssText = 'width:100%;background:#1a1a1a;border:1px solid #2a2a2a;color:#d4d4d4;padding:3px 6px;font-family:inherit;font-size:11px;';
        td2.appendChild(inp);
        var td3 = document.createElement('td');
        td3.className = 'box-tag';
        td3.textContent = it.source;
        var td4 = document.createElement('td');
        var btn = document.createElement('button');
        btn.textContent = '保存';
        btn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px;';
        btn.addEventListener('click', (function (k, input) {
            return function () { saveConfig(k, input.value); };
        })(it.key, inp));
        td4.appendChild(btn);
        tr.appendChild(td1); tr.appendChild(td2); tr.appendChild(td3); tr.appendChild(td4);
        configTableBody.appendChild(tr);
    }
}
function saveConfig(key, value) {
    fetch('/api/v1/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key: key, value: value })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (d.ok) {
                configMsg.textContent = '已保存 ' + key + ' @F' + d.frame + '（即时生效：llm.* 实时读取 / ui.* 前端刷新可见）';
                loadConfig();
            } else {
                configMsg.textContent = '失败: ' + (d.error || '未知错误');
            }
        })
        .catch(function (err) { configMsg.textContent = '请求失败: ' + err; });
}
document.getElementById('configRefresh').addEventListener('click', loadConfig);
document.getElementById('cfgAddBtn').addEventListener('click', function () {
    var k = document.getElementById('cfgNewKey').value.trim();
    var v = document.getElementById('cfgNewVal').value;
    if (k.length === 0) { return; }
    saveConfig(k, v);
});
loadConfig();
