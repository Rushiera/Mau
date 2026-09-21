// 延迟指令队列（design-ch4-delay §六）——外观层：列表 + 倒计时 + 新建 / 改时刻 / 取消。
// 数据源 GET /api/v1/delay（绝对 dueAt 毫秒）；倒计时本地自算（1 秒粒度，页面不可见时停表——dueAt 权威）。
// 写面复用 POST /api/v1/command（delay.add / delay.set / delay.cancel）——零新写端点。
var delayEntries = [];
var delayTimer = null;

// 队列状态拉取——列表数据源（页面加载 / 每次变更后调用）
function delayLoad() {
    fetch('/api/v1/delay')
        .then(function (r) { return r.json(); })
        .then(function (j) {
            delayEntries = (j && j.entries) ? j.entries : [];
            delayRender();
        })
        .catch(function (e) { uiWarn('延迟队列拉取', e); });
}

// 面板展开/收起
function delayToggle() {
    var pop = document.getElementById('delayPopover');
    if (!pop) { return; }
    if (pop.style.display === 'none' || pop.style.display === '') {
        delayLoad();
        pop.style.display = 'flex';
    } else {
        pop.style.display = 'none';
    }
}

// 倒计时文本——剩余时长（>1 天显示天+时）
function delayFmtRemain(ms) {
    var s = Math.floor(Math.max(0, ms) / 1000);
    var d = Math.floor(s / 86400);
    var h = Math.floor((s % 86400) / 3600);
    var m = Math.floor((s % 3600) / 60);
    var sec = s % 60;
    if (d > 0) { return d + '天' + h + '时'; }
    if (h > 0) { return h + ':' + delayPad(m) + ':' + delayPad(sec); }
    return m + ':' + delayPad(sec);
}

// 两位补零
function delayPad(n) {
    return (n < 10 ? '0' : '') + n;
}

// 到点时刻文本——本机时区 HH:mm:ss（今天只显时刻，跨天带日期）
function delayFmtDue(ms) {
    var d = new Date(ms);
    var now = new Date();
    var hm = delayPad(d.getHours()) + ':' + delayPad(d.getMinutes()) + ':' + delayPad(d.getSeconds());
    if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth() && d.getDate() === now.getDate()) {
        return hm;
    }
    return (d.getMonth() + 1) + '-' + delayPad(d.getDate()) + ' ' + hm;
}

// 来源标记
function delaySrcMark(source) {
    if (source === 'sleep') { return '💤'; }
    if (source === 'restart') { return '🔄'; }
    if (source === 'timer') { return '⏳'; }
    return '⏰';
}

// 行首文本——来源标记 + 到点时刻 + 循环/已响标记（渲染与测试共用单一出口）
function delayWhenText(e) {
    var mark = delaySrcMark(e.source) + ' ' + delayFmtDue(e.dueAt);
    if (e.loop) { mark = mark + ' 🔁'; }
    if (e.fired > 0) { mark = mark + ' 已响 ' + e.fired; }
    return mark;
}

// 列表渲染——重建（数据变化时调用）；倒计时由 delayTick 原地更新
function delayRender() {
    var list = document.getElementById('delayList');
    if (!list) { return; }
    var count = document.getElementById('delayCount');
    if (count) { count.textContent = delayEntries.length > 0 ? (' ' + delayEntries.length) : ''; }
    list.textContent = '';
    if (delayEntries.length === 0) {
        var empty = document.createElement('div');
        empty.className = 'delay-empty';
        empty.textContent = '（队列为空——下方输入时长与内容新建）';
        list.appendChild(empty);
        delaySyncTimer();
        return;
    }
    for (var i = 0; i < delayEntries.length; i = i + 1) {
        var e = delayEntries[i];
        var row = document.createElement('div');
        row.className = 'delay-row';
        row.setAttribute('data-id', String(e.id));
        var when = document.createElement('span');
        when.className = 'delay-when';
        when.textContent = delayWhenText(e);
        var cd = document.createElement('span');
        cd.className = 'delay-cd';
        cd.setAttribute('data-due', String(e.dueAt));
        cd.textContent = delayFmtRemain(e.dueAt - Date.now());
        var text = document.createElement('span');
        text.className = 'delay-text';
        text.textContent = e.content;
        var edit = document.createElement('button');
        edit.className = 'delay-act';
        edit.textContent = '改';
        edit.setAttribute('data-id', String(e.id));
        edit.addEventListener('click', delayEditClick);
        var loopBtn = document.createElement('button');
        loopBtn.className = 'delay-act';
        loopBtn.textContent = e.loop ? '🔁' : '单次';
        loopBtn.title = '切换循环（触发后按相同时长重排，不自动移除）';
        loopBtn.setAttribute('data-id', String(e.id));
        loopBtn.setAttribute('data-loop', e.loop ? '1' : '0');
        loopBtn.addEventListener('click', delayLoopClick);
        var cancel = document.createElement('button');
        cancel.className = 'delay-act';
        cancel.textContent = '×';
        cancel.setAttribute('data-id', String(e.id));
        cancel.addEventListener('click', delayCancelClick);
        row.appendChild(when);
        row.appendChild(cd);
        row.appendChild(text);
        row.appendChild(loopBtn);
        row.appendChild(edit);
        row.appendChild(cancel);
        list.appendChild(row);
    }
    delaySyncTimer();
}

// 倒计时刷新——原地更新（不重建 DOM；页面不可见时停表）
function delayTick() {
    var list = document.getElementById('delayList');
    if (!list) { return; }
    var nodes = list.getElementsByClassName('delay-cd');
    for (var i = 0; i < nodes.length; i = i + 1) {
        var due = parseInt(nodes[i].getAttribute('data-due'), 10);
        nodes[i].textContent = delayFmtRemain(due - Date.now());
    }
}

// 计时器同步——有可见条目且页面可见才跑表（空表/隐藏页零开销）
function delaySyncTimer() {
    var need = false;
    if (delayEntries.length > 0 && document.visibilityState !== 'hidden') {
        var pop = document.getElementById('delayPopover');
        if (pop && pop.style.display === 'flex') { need = true; }
    }
    if (need && delayTimer === null) {
        delayTimer = setInterval(delayTick, 1000);
    }
    if (!need && delayTimer !== null) {
        clearInterval(delayTimer);
        delayTimer = null;
    }
}

// 时长/时刻解析——支持 90s / 5m / 1h30m（相对）、HH:mm(:ss)（今天或明天）、yyyy-MM-dd HH:mm(:ss)（绝对）
function delayParseInput(raw) {
    var t = (raw || '').trim();
    if (t.length === 0) { return null; }
    // 相对时长——数字 + 单位（可组合）
    var re = /(\d+)\s*([hms])/gi;
    var total = 0;
    var matched = false;
    var m = re.exec(t);
    while (m !== null) {
        matched = true;
        var v = parseInt(m[1], 10);
        var unit = m[2].toLowerCase();
        if (unit === 'h') { total = total + v * 3600; }
        else if (unit === 'm') { total = total + v * 60; }
        else { total = total + v; }
        m = re.exec(t);
    }
    if (matched) { return Date.now() + total * 1000; }
    // 当日时刻——HH:mm(:ss)（已过则顺延明天）
    var hms = t.match(/^(\d{1,2}):(\d{2})(?::(\d{2}))?$/);
    if (hms !== null) {
        var d = new Date();
        d.setHours(parseInt(hms[1], 10), parseInt(hms[2], 10), hms[3] ? parseInt(hms[3], 10) : 0, 0);
        if (d.getTime() <= Date.now()) { d.setDate(d.getDate() + 1); }
        return d.getTime();
    }
    // 绝对时刻——yyyy-MM-dd HH:mm(:ss)
    var abs = Date.parse(t.replace(/-/g, '/'));
    if (!isNaN(abs)) { return abs; }
    return null;
}

// 新建——时长 + 内容 → delay.addat（绝对时刻，前端已换算）
function delayAdd() {
    var timeInput = document.getElementById('delayTime');
    var textInput = document.getElementById('delayText');
    if (!timeInput || !textInput) { return; }
    var content = textInput.value.trim();
    if (content.length === 0) { uiWarn('延迟指令新建', '内容为空'); return; }
    var dueAt = delayParseInput(timeInput.value);
    if (dueAt === null || dueAt <= 0) { uiWarn('延迟指令新建', '时长/时刻无法解析（示例：10m · 1h30m · 09:30）'); return; }
    var loopInput = document.getElementById('delayLoop');
    var line = 'delay.addat|' + String(dueAt) + '|' + content;
    if (loopInput && loopInput.checked) { line = 'delay.addatloop|' + String(dueAt) + '|' + content; }
    delaySend(line);
    timeInput.value = '';
    textInput.value = '';
}

// 改时刻——prompt 输入新时长/时刻（前端换算为绝对毫秒）
function delayEditClick(ev) {
    var id = ev.target.getAttribute('data-id');
    var input = window.prompt('新的时长或时刻（示例：10m · 1h30m · 09:30 · 2026-09-22 09:00:00）', '10m');
    if (input === null) { return; }
    var dueAt = delayParseInput(input);
    if (dueAt === null || dueAt <= 0) { uiWarn('延迟指令改时刻', '无法解析：' + input); return; }
    delaySend('delay.set|' + id + '|' + String(dueAt));
}

// 取消
function delayCancelClick(ev) {
    var id = ev.target.getAttribute('data-id');
    delaySend('delay.cancel|' + id);
}

// 循环开关——切换条目 loop 标记（触发后按相同时长重排）
function delayLoopClick(ev) {
    var id = ev.target.getAttribute('data-id');
    var cur = ev.target.getAttribute('data-loop');
    var next = cur === '1' ? '0' : '1';
    delaySend('delay.loop|' + id + '|' + next);
}

// 指令投递——写面复用 command 通道；随后重拉列表（列表为准）
function delaySend(line) {
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: line })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok) { uiWarn('延迟指令投递', d.error); }
            delayLoad();
        })
        .catch(function (e) { uiWarn('延迟指令投递', e); });
}

// 初始化——按钮/输入绑定 + 首次拉取 + 页面可见性联动
function delayInit() {
    var btn = document.getElementById('delayBtn');
    if (btn) { btn.addEventListener('click', function (e) { e.stopPropagation(); delayToggle(); }); }
    var addBtn = document.getElementById('delayAddBtn');
    if (addBtn) { addBtn.addEventListener('click', delayAdd); }
    var textInput = document.getElementById('delayText');
    if (textInput) {
        textInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { delayAdd(); }
        });
    }
    document.addEventListener('click', function (e) {
        var pop = document.getElementById('delayPopover');
        var wrap = document.getElementById('delayWrap');
        if (!pop || !wrap) { return; }
        if (pop.style.display !== 'flex') { return; }
        if (wrap.contains(e.target)) { return; }
        pop.style.display = 'none';
        delaySyncTimer();
    });
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'hidden') {
            if (delayTimer !== null) { clearInterval(delayTimer); delayTimer = null; }
        } else {
            delayTick();
            delaySyncTimer();
        }
    });
    delayLoad();
}
