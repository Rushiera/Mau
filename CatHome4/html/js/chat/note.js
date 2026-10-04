// ═══════════════════════════════════════════
// chat/note.js —— Note 面板（侧翼件 A169）
//
// 数据源：state 段的 note 字段（唯一状态源——state.js 的 stateRenderNote 钩子调入本件）
// 写面：指令总线（note.add <文本> / note.start）——复用 input.js 的 postCommand
// 交互：气泡（条数 + 当前任务摘要）点击展开弹层；再点 / 点外部收起；与定时弹层互斥
//
// 形态：气泡 + 弹层——DOM 在 chat.html（#noteWrap 段），样式 chat.css「M4c Note」段；本件零新增样式
// 载荷：{ tasks:[…], current, done, justCompleted }（后端 ChatSession.BuildNoteJson）
// ═══════════════════════════════════════════

/// 弹层展开态——只由点击控制（状态变动不自动展开）
var noteOpen = false;

/// 状态投影——state 段每次应用后调用（无容器时零动作）
function noteRenderFromState(note) {
    var d = note || {};
    var tasks = d.tasks || [];
    var current = d.current || 0;
    var text = document.getElementById('noteBubbleText');
    var title = document.getElementById('noteTitle');
    var list = document.getElementById('noteList');
    // 空计划——区分「刚全部完成」与「本来就是空」（Q7：完成态外观层匹配）
    if (tasks.length === 0) {
        var completed = (d.justCompleted === true);
        if (text) { text.textContent = completed ? '🎉 全部完成' : 'Note · 空'; }
        if (title) { title.textContent = completed ? 'Note · 全部完成' : 'Note · 空'; }
        if (list) {
            list.innerHTML = '<div class="note-row" style="color:var(--ch-fg-weak);font-size:var(--ch-fs-tag)">暂无计划——可手动新增任务</div>';
        }
        return;
    }
    // 气泡——条数 + 当前任务摘要（30 字截断——固定大小不撑布局）
    var summary = tasks[current] || '';
    if (summary.length > 30) {
        summary = summary.substring(0, 30) + '…';
    }
    if (text) { text.textContent = 'Note ' + (current + 1) + '/' + tasks.length + ' · ' + summary; }
    if (title) { title.textContent = 'Note (' + (current + 1) + '/' + tasks.length + ')'; }
    var html = '';
    for (var i = 0; i < tasks.length; i = i + 1) {
        var cls = 'note-row';
        var mark = '[ ]';
        if (i < current) {
            cls = cls + ' done';
            mark = '[x]';
        } else if (i === current) {
            cls = cls + ' current';
            mark = '[>]';
        }
        html = html + '<div class="' + cls + '"><span class="mark">' + mark + '</span><span>'
            + mdEscapeHtml(tasks[i]) + '</span></div>';
    }
    if (list) { list.innerHTML = html; }
}

/// 弹层开关——两弹层同位置，展开即收起定时（反向见 delay.js）
function noteToggle() {
    noteOpen = (noteOpen !== true);
    var pop = document.getElementById('notePopover');
    if (pop) { pop.style.display = noteOpen ? 'flex' : 'none'; }
    if (noteOpen && typeof delayClose === 'function') {
        delayClose();
    }
}

/// 收起——互斥面调用（幂等）
function noteClose() {
    if (noteOpen !== true) {
        return;
    }
    noteOpen = false;
    var pop = document.getElementById('notePopover');
    if (pop) { pop.style.display = 'none'; }
}

/// 新增任务——投递 note.add（列表刷新走 state 帧，前端不本地追加）
function noteAddSubmit() {
    var input = document.getElementById('noteAddInput');
    if (!input) {
        return;
    }
    var text = (input.value || '').replace(/^\s+|\s+$/g, '');
    if (text.length === 0) {
        return;
    }
    input.value = '';
    postCommand('note.add ' + text);
}

/// 开始 Note——把计划拼接进度推给 LLM 执行（忙时按钮禁用——stateSyncControls）
function noteStart() {
    postCommand('note.start');
}

/// 接线——气泡 / 按钮 / 输入 + 外部点击收起（本件加载于页面尾部，DOM 已就绪）
function noteBind() {
    var bubble = document.getElementById('noteBubble');
    if (bubble) {
        bubble.addEventListener('click', noteToggle);
    }
    var addBtn = document.getElementById('noteAddBtn');
    if (addBtn) {
        addBtn.addEventListener('click', noteAddSubmit);
    }
    var startBtn = document.getElementById('noteStartBtn');
    if (startBtn) {
        startBtn.addEventListener('click', noteStart);
    }
    var input = document.getElementById('noteAddInput');
    if (input) {
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                noteAddSubmit();
            }
        });
    }
    // 外部点击收起——展开态点击 wrap 外任意处关闭
    document.addEventListener('click', function (e) {
        if (noteOpen !== true) {
            return;
        }
        var wrap = document.getElementById('noteWrap');
        if (wrap && wrap.contains(e.target)) {
            return;
        }
        noteClose();
    });
}

noteBind();
