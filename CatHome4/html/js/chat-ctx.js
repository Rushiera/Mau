// CH4 外观层——chat-ctx.js：前文弹层（状态栏「前文 n 条 / n tokens / 前文关键信息」点击开的小窗口）
// 数据源：GET /api/v1/context（前文条目序列 = 送入 LLM 的真实消息；ctxTokens = 最近一次请求真实 prompt 值）
//         GET /api/v1/keyinfo（本次会话关键信息 = 旧会话留档同源四部分：加载报告 / user 消息 / 正式回复 / 轮结算）
//         GET /api/v1/fullctx（本次会话完整前文 = 送入 LLM 的全量消息落档；宿主读取前强制采集一次）
// 四视图：list（按条目顺序——点击条目展开全文）/ tokens（按字符规模降序——占比条）/ key（会话关键信息）/ full（完整前文）
// 口径：条目 token 无逐条真实值（API 只回总量）——tokens 视图按字符占比呈现并显式标注为估算，不假装精确

var chatCtxData = null;      // 前文响应（null=未加载；list / tokens 两视图共用）
var chatKeyData = null;      // 关键信息响应（null=未加载；key 视图用）
var chatFullData = null;     // 完整前文响应（null=未加载；full 视图用）
// 完整前文 token 预估系数——字符数 ÷ 系数 = 估算 token（实测口径：618.86k 字符 ÷ 388,861 真实 prompt ≈ 1.59，取 1.6）
var chatCtxCharsPerToken = 1.6;
var chatCtxMode = 'list';    // 当前视图——list（按条）/ tokens（按 token 分布）/ key（会话关键信息）/ full（完整前文）

// 三态：未开 → 打开 + 切到目标视图 + 读取；已开且同视图 → 收起（不重复读取）；已开且异视图 → 切视图 + 复用已读数据
function chatCtxOpen(mode) {
    var m = chatCtxModeOf(mode);
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
    // 已开时切视图复用该视图已读数据；新开一律重新读取（点一下展开是读取）
    chatCtxLoad(opened);
}

// 视图名归一——非法值回落 list（四个入口各传自己的视图名）
function chatCtxModeOf(mode) {
    if (mode === 'tokens') { return 'tokens'; }
    if (mode === 'key') { return 'key'; }
    if (mode === 'full') { return 'full'; }
    return 'list';
}

// 窗口提示——条目被截断时标注窗口形态（首条恒在窗口内：显示首条 + 尾部；否则显示尾部）
function chatCtxWindowNote(d, items) {
    if (!d || items.length >= d.count) { return ''; }
    if (items.length > 0 && items[0].i === 1) { return '（显示首条 + 尾部 ' + (items.length - 1) + ' 条）'; }
    return '（显示尾部 ' + items.length + ' 条）';
}

// 数据装载——按当前视图选数据面（key → /api/v1/keyinfo；full → /api/v1/fullctx；其余 → /api/v1/context）；reuse=true 且有缓存直接重渲染
// 三视图统一不带 ?max——窗口随端点缺省（配置项 chat.ctx_view_max，默认 200 · 上限 2000）；首条恒在窗口内（1.7.26 加固），尾部窗口不再挤出 system 注入块
function chatCtxLoad(reuse) {
    if (chatCtxMode === 'key') {
        if (reuse && chatKeyData) { chatCtxRender(); return; }
        chatCtxFetch('/api/v1/keyinfo', '关键信息', function (d) { chatKeyData = d; });
        return;
    }
    if (chatCtxMode === 'full') {
        if (reuse && chatFullData) { chatCtxRender(); return; }
        chatCtxFetch('/api/v1/fullctx', '完整前文', function (d) { chatFullData = d; });
        return;
    }
    if (reuse && chatCtxData) { chatCtxRender(); return; }
    chatCtxFetch('/api/v1/context', '前文', function (d) { chatCtxData = d; });
}

// 读取——失败出声（不静默留白）
function chatCtxFetch(url, label, apply) {
    var list = document.getElementById('ctxList');
    if (list) { list.textContent = '读取' + label + '…'; }
    fetch(url)
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apply(d || null);
            chatCtxRender();
        })
        .catch(function (e) {
            apply(null);
            var l = document.getElementById('ctxList');
            if (l) { l.textContent = label + '读取失败——' + e; }
        });
}

function chatCtxClose() {
    var pop = document.getElementById('ctxPopover');
    if (pop) { pop.style.display = 'none'; }
}

/**
 * 刷新（弹层右上按钮）——两步：
 * ① 完整前文采集就绪——读一次 /api/v1/fullctx（端点读取前强制采集，不受 30 秒节流滞后影响）
 * ② 完全重建会话页面——全量重拉历史（含首块注入报告），不走 200 块窗口
 */
function chatCtxRefresh() {
    chatCtxFetch('/api/v1/fullctx', '完整前文', function (d) { chatFullData = d; });
    chatLoadHistory(true);
}

function chatCtxSwitch(mode) {
    var m = chatCtxModeOf(mode);
    if (m === chatCtxMode) { return; }
    chatCtxMode = m;
    chatCtxModeMark();
    // 已开状态下切视图——复用该视图已读数据（未读则读一次）
    chatCtxLoad(true);
}

// 视图标签高亮——当前视图加 ctx-mode-on
function chatCtxModeMark() {
    var l = document.getElementById('ctxModeList');
    var t = document.getElementById('ctxModeTokens');
    var k = document.getElementById('ctxModeKey');
    var f = document.getElementById('ctxModeFull');
    if (l) { l.classList.toggle('ctx-mode-on', chatCtxMode === 'list'); }
    if (t) { t.classList.toggle('ctx-mode-on', chatCtxMode === 'tokens'); }
    if (k) { k.classList.toggle('ctx-mode-on', chatCtxMode === 'key'); }
    if (f) { f.classList.toggle('ctx-mode-on', chatCtxMode === 'full'); }
}

function chatCtxRender() {
    var list = document.getElementById('ctxList');
    var meta = document.getElementById('ctxMeta');
    var title = document.getElementById('ctxTitle');
    if (!list) { return; }
    list.textContent = '';
    if (chatCtxMode === 'key') {
        // 关键信息视图——独立数据面（chatKeyData）
        chatCtxRenderKey(list, meta, title);
        return;
    }
    if (chatCtxMode === 'full') {
        // 完整前文视图——独立数据面（chatFullData）
        chatCtxRenderFull(list, meta, title);
        return;
    }
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
            meta.textContent = '真实 ' + tokText + '（最近一次请求）· 前文 ' + chatFmtCount(d.chars) + ' 字符 · 分布按字符占比（估算）'
                + chatCtxWindowNote(d, items);
        }
        chatCtxRenderTokens(list, items);
    } else {
        if (title) { title.textContent = '前文条目'; }
        if (meta) {
            meta.textContent = '共 ' + chatFmtCount(d.count) + ' 条 · ' + chatFmtCount(d.chars) + ' 字符 · 真实 ' + tokText
                + chatCtxWindowNote(d, items);
        }
        chatCtxRenderList(list, items);
    }
}

// key 视图——本次会话关键信息（加载报告 / user 消息 / 正式回复 / 轮结算；与旧会话留档同源）
// 条目样式与前文弹层同款（复用 ctx-item 族），role 标签走中文名
var chatKeyRoleLabels = { user: '用户', reply: '回复', report: '加载报告', roundsum: '轮结算' };

function chatCtxRenderKey(list, meta, title) {
    var d = chatKeyData;
    if (title) { title.textContent = '前文关键信息'; }
    if (!d || d.ok !== true) {
        if (meta) { meta.textContent = ''; }
        list.textContent = '关键信息不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    if (meta) {
        meta.textContent = '共 ' + chatFmtCount(d.count) + ' 条 · ' + chatFmtCount(d.chars) + ' 字符'
            + chatCtxWindowNote(d, items);
    }
    chatCtxRenderList(list, items, '本次会话暂无关键信息', chatKeyRoleLabels);
}

// full 视图——本次会话完整前文（送入 LLM 的全量消息；宿主侧留档文本剥离修饰后还原）
// 与 key 视图独立并行：各自端点、各自数据面；条目样式与前文弹层同款（复用 ctx-item 族）
var chatFullRoleLabels = { system: '系统', user: '用户', assistant: '助手', tool: '工具' };

function chatCtxRenderFull(list, meta, title) {
    var d = chatFullData;
    if (title) { title.textContent = '完整前文'; }
    if (!d || d.ok !== true) {
        if (meta) { meta.textContent = ''; }
        list.textContent = '完整前文不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    if (meta) {
        // 统计口径——字符数 ÷ 系数 = 预估 token（完整前文是留档，不参与请求，无真实 prompt 值）
        meta.textContent = '共 ' + chatFmtCount(d.count) + ' 条 · 约 '
            + chatFmtCount(Math.round((d.chars || 0) / chatCtxCharsPerToken)) + ' tokens（估算）· '
            + chatFmtCount(d.chars) + ' 字符'
            + chatCtxWindowNote(d, items);
    }
    chatCtxRenderList(list, items, '本次会话暂无完整前文', chatFullRoleLabels);
}

// list 视图——按条目顺序；点击条目头展开全文（折叠态显示摘要）
function chatCtxRenderList(list, items, emptyText, roleLabels) {
    for (var i = 0; i < items.length; i++) {
        var it = items[i];
        var row = document.createElement('div');
        row.className = 'ctx-item';
        var head = document.createElement('div');
        head.className = 'ctx-item-head';
        head.appendChild(chatCtxRoleTag(it, roleLabels));
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
    if (items.length === 0) { list.textContent = emptyText || '前文为空'; }
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

function chatCtxRoleTag(it, roleLabels) {
    var role = it.role || 'user';
    var tag = document.createElement('span');
    tag.className = 'ctx-role ctx-role-' + role;
    tag.textContent = (roleLabels && roleLabels[role]) ? roleLabels[role] : role;
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
    var refresh = document.getElementById('ctxRefresh');
    var ml = document.getElementById('ctxModeList');
    var mt = document.getElementById('ctxModeTokens');
    var mk = document.getElementById('ctxModeKey');
    var mf = document.getElementById('ctxModeFull');
    if (refresh) { refresh.addEventListener('click', chatCtxRefresh); }
    if (ml) { ml.addEventListener('click', function () { chatCtxSwitch('list'); }); }
    if (mt) { mt.addEventListener('click', function () { chatCtxSwitch('tokens'); }); }
    if (mk) { mk.addEventListener('click', function () { chatCtxSwitch('key'); }); }
    if (mf) { mf.addEventListener('click', function () { chatCtxSwitch('full'); }); }
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
