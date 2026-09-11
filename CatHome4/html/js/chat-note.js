// CH4 外观层——chat-note.js：Note 面板核心（M4c 悬浮气泡）——F2.3 自 chat.html 内联拆出（模块化；对话逻辑唯一真相源）
// 加载顺序：chat-core.js → chat-note.js（chat.html 引导层引用）；依赖 chat-core.js 的 chatState/chatSetState/chatKeepAlive/viewContainers/escapeHtml
// 单向数据流：Note 状态 = SSE note 事件 + GET /api/v1/note 兜底；写通道 = note.add/note.start 走 command 总线（无飞线）
// SSE/按钮绑定/初始化由 chat.html 引导层承接（EventSource note + 防御式 addEventListener + fetch note）

// [段4] Note——Q3 v2（2026-09-08）：输入区上方悬浮按钮（条数+摘要，固定大小不占布局）+ 点击展开气泡区域（popover 贴按钮上方；再点按钮/点外部收起——高度变化零影响聊天区）；完成态显示 🎉 全部完成
var noteState = { tasks: [], current: 0, done: 0, justCompleted: false };
var noteModalOpen = false;

function noteRender() {
    var tasks = noteState.tasks || [];
    var current = noteState.current || 0;
    var bubble = document.getElementById('noteBubbleText');
    var title = document.getElementById('noteTitle');
    var list = document.getElementById('noteList');
    // 无任务——空提示 / 刚全部完成（Q7：done 态外观层匹配——🎉 全部完成）
    if (tasks.length === 0) {
        if (noteState.justCompleted === true) {
            if (bubble) { bubble.textContent = '🎉 全部完成'; }
            if (title) { title.textContent = 'Note · 全部完成'; }
        } else {
            if (bubble) { bubble.textContent = 'Note · 空'; }
            if (title) { title.textContent = 'Note · 空'; }
        }
        if (list) { list.innerHTML = '<div class="note-row" style="color:#5a5a5a;font-size:12px">暂无计划——可手动新增任务</div>'; }
        return;
    }
    // 按钮——条数 + 当前任务摘要（30 字截断——固定大小不撑布局）
    var summary = tasks[current] || '';
    if (summary.length > 30) { summary = summary.substring(0, 30) + '…'; }
    if (bubble) { bubble.textContent = 'Note ' + (current + 1) + '/' + tasks.length + ' · ' + summary; }
    if (title) { title.textContent = 'Note (' + (current + 1) + '/' + tasks.length + ')'; }
    var html = '';
    for (var i = 0; i < tasks.length; i++) {
        var cls = 'note-row';
        var mark = '[ ]';
        if (i < current) { cls = cls + ' done'; mark = '[x]'; }
        else if (i === current) { cls = cls + ' current'; mark = '[>]'; }
        html += '<div class="' + cls + '"><span class="mark">' + mark + '</span><span>' + escapeHtml(tasks[i]) + '</span></div>';
    }
    if (list) { list.innerHTML = html; }
}

function noteToggle() {
    noteModalOpen = !noteModalOpen;
    var pop = document.getElementById('notePopover');
    if (pop) { pop.style.display = noteModalOpen ? 'block' : 'none'; }
    if (noteModalOpen) { noteRender(); }
}

// 外部点击收起——popover 展开时点击 noteWrap 外任意处关闭（网页常见 i 详情交互）
document.addEventListener('click', function (e) {
    if (!noteModalOpen) { return; }
    var wrap = document.getElementById('noteWrap');
    if (wrap && wrap.contains(e.target)) { return; }
    noteToggle();
});

function noteAdd() {
    var input = document.getElementById('noteAddInput');
    var text = input.value.trim();
    if (text.length === 0) { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'note.add ' + text })
    }).then(function () {
        input.value = '';
    }).catch(function (e) { uiWarn('Note 任务新增', e); });
}

function noteStart() {
    if (chatState !== 'idle') { return; }
    if (!noteState.tasks || noteState.tasks.length === 0) { return; }
    // 进入 sending 态——note.start 后 view 流事件正常渲染（chatOnView 检查 chatState；idle 态会丢弃全部流事件）
    viewContainers = {};
    chatSetState('sending');
    chatKeepAlive();
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'note.start' })
    }).catch(function (e) { uiWarn('Note 启动指令投递', e); });
}

function noteOnEvent(d) {
    if (d.state) {
        noteState = d.state;
        // 任务存在时自动展开 modal（工作进度面板语义——人写入/计划推送即弹出）；无任务（空/刚完成）不弹
        if (noteState.tasks && noteState.tasks.length > 0 && !noteModalOpen) {
            noteToggle();
        }
    }
    noteRender();
}
