// CH4 外观层——chat-note.js：Note 面板核心（M4c 悬浮气泡）——F2.3 自 chat.html 内联拆出（模块化；对话逻辑唯一真相源）
// 加载顺序：chat-core.js → chat-note.js（chat.html 引导层引用）；依赖 chat-core.js 的 chatState/chatSetState/chatKeepAlive/viewContainers/escapeHtml
// 单向数据流：Note 状态 = SSE note 事件 + GET /api/v1/note 兜底；写通道 = note.add/note.start 走 command 总线（无飞线）
// SSE/按钮绑定/初始化由 chat.html 引导层承接（EventSource note + 防御式 addEventListener + fetch note）

// [段4] Note 面板（M4c——缩略一行/展开自适应；新增任务走 command 总线）
var noteState = { tasks: [], current: 0, done: 0 };
var noteExpanded = false;

function noteRender() {
    var tasks = noteState.tasks || [];
    var current = noteState.current || 0;
    var title = document.getElementById('noteTitle');
    var list = document.getElementById('noteList');
    if (tasks.length === 0) {
        title.textContent = 'Note · 空';
        list.innerHTML = '<div class="note-row" style="color:#5a5a5a">暂无计划</div>';
        return;
    }
    title.textContent = 'Note (' + (current + 1) + '/' + tasks.length + ')';
    var html = '';
    for (var i = 0; i < tasks.length; i++) {
        var cls = 'note-row';
        var mark = '[ ]';
        if (i < current) { cls = cls + ' done'; mark = '[x]'; }
        else if (i === current) { cls = cls + ' current'; mark = '[>]'; }
        html += '<div class="' + cls + '"><span class="mark">' + mark + '</span><span>' + escapeHtml(tasks[i]) + '</span></div>';
    }
    list.innerHTML = html;
}

function noteToggle() {
    noteExpanded = !noteExpanded;
    var panel = document.getElementById('notePanel');
    if (noteExpanded) { panel.classList.remove('collapsed'); document.getElementById('noteToggleBtn').textContent = '▾'; }
    else { panel.classList.add('collapsed'); document.getElementById('noteToggleBtn').textContent = '▸'; }
}

function noteAdd() {
    var input = document.getElementById('noteAddInput');
    var text = input.value.trim();
    if (text.length === 0) { return; }
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: 'note.add ' + text })
    }).then(function () { input.value = ''; });
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
    });
}

function noteOnEvent(d) {
    if (d.state) {
        noteState = d.state;
        // 手写/计划存在时自动展开面板（工作进度面板语义——人写入即弹出）
        if (noteState.tasks && noteState.tasks.length > 0 && !noteExpanded) {
            noteToggle();
        }
    }
    noteRender();
}
