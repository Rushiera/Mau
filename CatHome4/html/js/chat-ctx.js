// CH4 外观层——chat-ctx.js：前文弹层（状态栏「前文 n 条 / n tokens」点击开的小窗口）
// 数据源：GET /api/v1/context（前文条目序列 = 送入 LLM 的真实消息；ctxTokens = 最近一次请求真实 prompt 值）
// 两视图：list（按条目顺序——点击条目展开全文）/ tokens（按字符规模降序——占比条）
// 口径：条目 token 无逐条真实值（API 只回总量）——tokens 视图按字符占比呈现并显式标注为估算，不假装精确

var chatCtxData = null;      // 最近一次响应（null=未加载）
var chatCtxMode = 'list';    // 当前视图——list（按条）/ tokens（按 token 分布）

// 三态：未开 → 打开 + 切到目标视图 + 读取；已开且同视图 → 收起（不重复读取）；已开且异视图 → 切视图 + 复用已读数据
function chatCtxOpen(mode) {
    var m = (mode === 'tokens') ? 'tokens' : 'list';
    var pop = document.getElementById('ctxPopover');
    if (!pop) { return; }
    var opened = (pop.style.display !== 'none');
    if (opened && chatCtxMode === m) {
        chatCtxClose();
        return;
    }
    chatCtxMode = m;
    pop.style.display = 'block';
    chatCtxModeMark();
    if (opened && chatCtxData) {
        // 切视图——同一份前文快照直接重渲染，不再发请求
        chatCtxRender();
        return;
    }
    var list = document.getElementById('ctxList');
    if (list) { list.textContent = '读取前文…'; }
    fetch('/api/v1/context')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            chatCtxData = d || null;
            chatCtxRender();
        })
        .catch(function (e) {
            chatCtxData = null;
            var l = document.getElementById('ctxList');
            if (l) { l.textContent = '前文读取失败——' + e; }
        });
}

function chatCtxClose() {
    var pop = document.getElementById('ctxPopover');
    if (pop) { pop.style.display = 'none'; }
}

function chatCtxSwitch(mode) {
    chatCtxMode = (mode === 'tokens') ? 'tokens' : 'list';
    chatCtxModeMark();
    chatCtxRender();
}

// 视图标签高亮——当前视图加 ctx-mode-on
function chatCtxModeMark() {
    var l = document.getElementById('ctxModeList');
    var t = document.getElementById('ctxModeTokens');
    if (l) { l.classList.toggle('ctx-mode-on', chatCtxMode === 'list'); }
    if (t) { t.classList.toggle('ctx-mode-on', chatCtxMode === 'tokens'); }
}

function chatCtxRender() {
    var list = document.getElementById('ctxList');
    var meta = document.getElementById('ctxMeta');
    var title = document.getElementById('ctxTitle');
    if (!list) { return; }
    list.textContent = '';
    var d = chatCtxData;
    if (!d || d.ok !== true) {
        if (title) { title.textContent = '前文'; }
        if (meta) { meta.textContent = ''; }
        list.textContent = '前文不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    var tokText = (d.ctxTokens > 0) ? (chatFmtCount(d.ctxTokens) + ' tokens') : '未知';
    if (chatCtxMode === 'tokens') {
        if (title) { title.textContent = '前文 token 明细'; }
        if (meta) {
            meta.textContent = '真实 ' + tokText + '（最近一次请求）· 前文 ' + chatFmtCount(d.chars) + ' 字符 · 分布按字符占比（估算）';
        }
        chatCtxRenderTokens(list, items);
    } else {
        if (title) { title.textContent = '前文条目'; }
        if (meta) {
            meta.textContent = '共 ' + chatFmtCount(d.count) + ' 条 · ' + chatFmtCount(d.chars) + ' 字符 · 真实 ' + tokText
                + (items.length < d.count ? ('（显示尾部 ' + items.length + ' 条）') : '');
        }
        chatCtxRenderList(list, items);
    }
}

// list 视图——按条目顺序；点击条目头展开全文（折叠态显示摘要）
function chatCtxRenderList(list, items) {
    for (var i = 0; i < items.length; i++) {
        var it = items[i];
        var row = document.createElement('div');
        row.className = 'ctx-item';
        var head = document.createElement('div');
        head.className = 'ctx-item-head';
        head.appendChild(chatCtxRoleTag(it));
        head.appendChild(chatCtxText('span', 'ctx-i', '#' + it.i));
        head.appendChild(chatCtxText('span', 'ctx-size', chatFmtCount(it.chars) + ' 字符' + (it.truncated ? '（已截断）' : '')));
        if (it.tool) { head.appendChild(chatCtxText('span', 'ctx-tool', it.tool)); }
        if (it.time > 0) { head.appendChild(chatCtxText('span', 'ctx-time', chatCtxFmtTime(it.time))); }
        row.appendChild(head);
        var body = document.createElement('div');
        body.className = 'ctx-body';
        body.textContent = it.preview || '（空）';
        row.appendChild(body);
        var full = document.createElement('pre');
        full.className = 'ctx-full';
        full.style.display = 'none';
        full.textContent = it.content || '（空）';
        row.appendChild(full);
        chatCtxBindToggle(head, row, body, full);
        list.appendChild(row);
    }
    if (items.length === 0) { list.textContent = '前文为空'; }
}

// tokens 视图——按字符规模降序；占比 = 该条字符 / 展示条目字符合计（估算口径）
function chatCtxRenderTokens(list, items) {
    var total = 0;
    for (var i = 0; i < items.length; i++) { total = total + (items[i].chars || 0); }
    var sorted = items.slice().sort(function (a, b) { return (b.chars || 0) - (a.chars || 0); });
    for (var k = 0; k < sorted.length; k++) {
        var it = sorted[k];
        var pct = total > 0 ? (it.chars || 0) / total * 100 : 0;
        var row = document.createElement('div');
        row.className = 'ctx-item ctx-item-tok';
        var head = document.createElement('div');
        head.className = 'ctx-item-head';
        head.appendChild(chatCtxRoleTag(it));
        head.appendChild(chatCtxText('span', 'ctx-i', '#' + it.i));
        head.appendChild(chatCtxText('span', 'ctx-size', chatFmtCount(it.chars) + ' 字符'));
        if (it.tool) { head.appendChild(chatCtxText('span', 'ctx-tool', it.tool)); }
        head.appendChild(chatCtxText('span', 'ctx-pct', pct.toFixed(1) + '%'));
        row.appendChild(head);
        var bar = document.createElement('div');
        bar.className = 'ctx-bar';
        var fill = document.createElement('div');
        fill.className = 'ctx-bar-fill ctx-role-bg-' + (it.role || 'user');
        fill.style.width = (pct < 0.4 ? 0.4 : pct) + '%';
        bar.appendChild(fill);
        row.appendChild(bar);
        list.appendChild(row);
    }
    if (items.length === 0) { list.textContent = '前文为空'; }
}

// 条目展开/折叠——头行点击切换（摘要 ↔ 全文）
function chatCtxBindToggle(head, row, body, full) {
    head.addEventListener('click', function () {
        var open = (full.style.display !== 'none');
        full.style.display = open ? 'none' : 'block';
        body.style.display = open ? 'block' : 'none';
        row.classList.toggle('open', !open);
    });
}

function chatCtxRoleTag(it) {
    var tag = document.createElement('span');
    tag.className = 'ctx-role ctx-role-' + (it.role || 'user');
    tag.textContent = it.role || '?';
    return tag;
}

function chatCtxText(tag, cls, text) {
    var el = document.createElement(tag);
    el.className = cls;
    el.textContent = text;
    return el;
}

// 条目时刻——Unix 毫秒 → HH:mm:ss（本地时区）
function chatCtxFmtTime(ms) {
    var d = new Date(ms);
    var hh = d.getHours();
    var mm = d.getMinutes();
    var ss = d.getSeconds();
    return (hh < 10 ? '0' : '') + hh + ':' + (mm < 10 ? '0' : '') + mm + ':' + (ss < 10 ? '0' : '') + ss;
}

// 事件绑定——弹层控件（脚本位于元素之后，元素已就位）
(function () {
    var pop = document.getElementById('ctxPopover');
    var close = document.getElementById('ctxClose');
    var ml = document.getElementById('ctxModeList');
    var mt = document.getElementById('ctxModeTokens');
    if (close) { close.addEventListener('click', chatCtxClose); }
    if (ml) { ml.addEventListener('click', function () { chatCtxSwitch('list'); }); }
    if (mt) { mt.addEventListener('click', function () { chatCtxSwitch('tokens'); }); }
    if (pop) {
        // 弹层内点击不冒泡到文档（避免被「点外部关闭」误判）
        pop.addEventListener('click', function (ev) { ev.stopPropagation(); });
        document.addEventListener('click', function (ev) {
            if (pop.style.display === 'none') { return; }
            if (pop.contains(ev.target)) { return; }
            chatCtxClose();
        });
    }
    document.addEventListener('keydown', function (ev) {
        if (ev.key === 'Escape' && pop && pop.style.display !== 'none') { chatCtxClose(); }
    });
})();
