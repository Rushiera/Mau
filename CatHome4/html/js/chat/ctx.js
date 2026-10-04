// ═══════════════════════════════════════════
// chat/ctx.js —— 前文弹层（侧翼件）
//
// 定位：顶栏信息位三段（前文条数 / 前文 tokens / 关键信息）与弹层四视图的**唯一实现处**。
// 数据源（只读端点——后端已就位，本件零新增后端面）：
//   GET /api/v1/context   前文条目（送入 LLM 的真实消息；ctxTokens = 最近一次请求真实 prompt 值）
//   GET /api/v1/keyinfo   本次会话关键信息（加载报告 / user 消息 / 正式回复 / 轮结算——与旧会话留档同源）
//   GET /api/v1/fullctx   本次会话完整前文（送入 LLM 的全量消息；宿主读取前强制采集一次）
// 四视图：list（按条目顺序 · 点击条目展开全文）/ tokens（按字符规模降序 · 占比条）/ key（关键信息）/ full（完整前文）
// 口径：逐条 token 无真实值（端点只回总量）——tokens 视图按字符占比呈现并显式标注为估算，不假装精确
// 形态：DOM 在 chat.html（#ctxPopover 段），样式 chat.css「前文弹层」段；本件只填内容，零新增结构
// 入口：信息位 `[data-ctx]` 段点击（委托）· 弹层内四页签 · ⟳ 刷新；关闭走 Esc / 点弹层外
// 边界：数据全部来自后端端点（前端零推断）；失败在弹层内出声 + 通知面告警（不静默留白）
// ═══════════════════════════════════════════

/// 数据面——三视图各持一份（null = 未加载；list / tokens 共用 context 面）
var ctxData = null;
var ctxKeyData = null;
var ctxFullData = null;

/// 当前视图——list（按条）/ tokens（按 token 分布）/ key（关键信息）/ full（完整前文）
var ctxMode = 'list';

/// 条目角色中文名——key 视图四类（与旧会话留档同源）
var CTX_KEY_ROLES = { user: '用户', reply: '回复', report: '加载报告', roundsum: '轮结算' };

/// 条目角色中文名——full 视图四类（LLM 消息角色）
var CTX_FULL_ROLES = { system: '系统', user: '用户', assistant: '助手', tool: '工具' };

/// 视图名归一——非法值回落 list（各入口各传自己的视图名）
function ctxModeOf(mode) {
    if (mode === 'tokens') { return 'tokens'; }
    if (mode === 'key') { return 'key'; }
    if (mode === 'full') { return 'full'; }
    return 'list';
}

/// 打开 / 切换 / 收起——三态：未开 → 打开并读取；已开同视图 → 收起（不重复读取）；已开异视图 → 切视图复用缓存
function ctxOpen(mode) {
    var m = ctxModeOf(mode);
    var pop = document.getElementById('ctxPopover');
    if (!pop) {
        return;
    }
    var opened = (pop.style.display !== 'none' && pop.style.display !== '');
    if (opened && ctxMode === m) {
        ctxClose();
        return;
    }
    ctxMode = m;
    pop.style.display = 'flex';
    ctxModeMark();
    // 已开时切视图复用该视图已读数据；新开一律重新读取（点一下展开即读取）
    ctxLoad(opened);
}

/// 收起——幂等
function ctxClose() {
    var pop = document.getElementById('ctxPopover');
    if (pop) {
        pop.style.display = 'none';
    }
}

/// 页签切换——同视图零动作；异视图切并复用缓存
function ctxSwitch(mode) {
    var m = ctxModeOf(mode);
    if (m === ctxMode) {
        return;
    }
    ctxMode = m;
    ctxModeMark();
    ctxLoad(true);
}

/// 页签高亮——当前视图加 ctx-mode-on
function ctxModeMark() {
    var modes = ['list', 'tokens', 'key', 'full'];
    for (var i = 0; i < modes.length; i = i + 1) {
        var node = document.getElementById(ctxModeId(modes[i]));
        if (node) {
            node.classList.toggle('ctx-mode-on', ctxMode === modes[i]);
        }
    }
}

/// 页签元素 id——视图名 → 元素 id（表驱动单点）
function ctxModeId(mode) {
    if (mode === 'tokens') { return 'ctxModeTokens'; }
    if (mode === 'key') { return 'ctxModeKey'; }
    if (mode === 'full') { return 'ctxModeFull'; }
    return 'ctxModeList';
}

/// 刷新（弹层右上 ⟳）——两步：① 完整前文采集就绪（端点读取前强制采集，不受节流滞后影响）
/// ② 重建会话页面（断开重连 → 服务端重推全量帧，零本地状态修补）
function ctxRefresh() {
    ctxFetch('/api/v1/fullctx', '完整前文', function (d) { ctxFullData = d; });
    if (typeof chatRefresh === 'function') {
        chatRefresh();
    }
}

/// 数据装载——按当前视图选数据面（key → keyinfo；full → fullctx；其余 → context）；reuse=true 且有缓存直接重渲染
/// 三视图统一不带 ?max——窗口随端点缺省（缺省不限 = 全量回传；首条恒在窗口内）
function ctxLoad(reuse) {
    if (ctxMode === 'key') {
        if (reuse && ctxKeyData) { ctxRender(); return; }
        ctxFetch('/api/v1/keyinfo', '关键信息', function (d) { ctxKeyData = d; });
        return;
    }
    if (ctxMode === 'full') {
        if (reuse && ctxFullData) { ctxRender(); return; }
        ctxFetch('/api/v1/fullctx', '完整前文', function (d) { ctxFullData = d; });
        return;
    }
    if (reuse && ctxData) { ctxRender(); return; }
    ctxFetch('/api/v1/context', '前文', function (d) { ctxData = d; });
}

/// 读取——读取中留痕；失败出声（弹层内写原因 + 通知面告警，不静默留白）
function ctxFetch(url, label, apply) {
    var list = document.getElementById('ctxList');
    if (list) { list.textContent = '读取' + label + '…'; }
    fetch(url)
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apply(d || null);
            ctxRender();
        })
        .catch(function (e) {
            apply(null);
            var l = document.getElementById('ctxList');
            if (l) { l.textContent = label + '读取失败——' + e; }
            if (typeof warn === 'function') { warn(label + '读取失败', e); }
        });
}

/// 渲染——按当前视图分派（数据面缺 / 端点报错 → 弹层内出声）
function ctxRender() {
    var list = document.getElementById('ctxList');
    var meta = document.getElementById('ctxMeta');
    var title = document.getElementById('ctxTitle');
    if (!list) {
        return;
    }
    list.textContent = '';
    if (ctxMode === 'key') {
        ctxRenderKey(list, meta, title);
        return;
    }
    if (ctxMode === 'full') {
        ctxRenderFull(list, meta, title);
        return;
    }
    var d = ctxData;
    if (!d || d.ok !== true) {
        if (title) { title.textContent = '前文'; }
        if (meta) { meta.textContent = ''; }
        list.textContent = '前文不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    var tokText = (d.ctxTokens > 0) ? (fmtCount(d.ctxTokens) + ' tokens') : '未知';
    if (ctxMode === 'tokens') {
        if (title) { title.textContent = '前文 token 明细'; }
        if (meta) {
            meta.textContent = '真实 ' + tokText + '（最近一次请求）· 前文 ' + fmtCount(d.chars) + ' 字符 · 分布按字符占比（估算）'
                + ctxWindowNote(d, items);
        }
        ctxRenderTokens(list, items);
        return;
    }
    if (title) { title.textContent = '前文条目'; }
    if (meta) {
        meta.textContent = '共 ' + fmtCount(d.count) + ' 条 · ' + fmtCount(d.chars) + ' 字符 · 真实 ' + tokText
            + ctxWindowNote(d, items);
    }
    ctxRenderList(list, items, '前文为空');
}

/// key 视图——本次会话关键信息（加载报告 / user 消息 / 正式回复 / 轮结算；条目样式与前文弹层同款）
function ctxRenderKey(list, meta, title) {
    var d = ctxKeyData;
    if (title) { title.textContent = '前文关键信息'; }
    if (!d || d.ok !== true) {
        if (meta) { meta.textContent = ''; }
        list.textContent = '关键信息不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    if (meta) {
        meta.textContent = '共 ' + fmtCount(d.count) + ' 条 · ' + fmtCount(d.chars) + ' 字符' + ctxWindowNote(d, items);
    }
    ctxRenderList(list, items, '本次会话暂无关键信息', CTX_KEY_ROLES);
}

/// full 视图——本次会话完整前文（送入 LLM 的全量消息；宿主侧留档文本剥离修饰后还原）
function ctxRenderFull(list, meta, title) {
    var d = ctxFullData;
    if (title) { title.textContent = '完整前文'; }
    if (!d || d.ok !== true) {
        if (meta) { meta.textContent = ''; }
        list.textContent = '完整前文不可读：' + ((d && d.error) || '宿主未响应 / 端点未注册');
        return;
    }
    var items = d.items || [];
    if (meta) {
        meta.textContent = '共 ' + fmtCount(d.count) + ' 条 · ' + ctxFullTokensText(d) + ' · ' + fmtCount(d.chars) + ' 字符'
            + ctxWindowNote(d, items);
    }
    ctxRenderList(list, items, '本次会话暂无完整前文', CTX_FULL_ROLES);
}

/// token 文案——宿主折算值直显；缺省回落由宿主标注 estimated，前端原样转述不冒充实测
function ctxFullTokensText(d) {
    var tok = Number(d.tokens) || 0;
    if (tok <= 0) { return 'token 未知（宿主未回折算值）'; }
    if (d.estimated) { return '约 ' + fmtCount(tok) + ' tokens（缺省比值 2.4 折算——本会话无实测）'; }
    return '约 ' + fmtCount(tok) + ' tokens（实测比值 ' + (Number(d.ratio) || 0).toFixed(2) + ' 折算）';
}

/// 窗口提示——条目被截断时标注窗口形态（首条恒在窗口内：显示首条 + 尾部；否则显示尾部）
function ctxWindowNote(d, items) {
    if (!d || items.length >= d.count) { return ''; }
    if (items.length > 0 && items[0].i === 1) { return '（显示首条 + 尾部 ' + (items.length - 1) + ' 条）'; }
    return '（显示尾部 ' + items.length + ' 条）';
}

/// list 视图——按条目顺序；点击条目头展开全文（折叠态显示摘要）
function ctxRenderList(list, items, emptyText, roleLabels) {
    for (var i = 0; i < items.length; i = i + 1) {
        list.appendChild(ctxItem(items[i], roleLabels));
    }
    if (items.length === 0) { list.textContent = emptyText || '前文为空'; }
}

/// 单条条目——头行（角色 / 序号 / 字符数 / 工具 / 时刻）+ 摘要 + 全文（折叠）
function ctxItem(it, roleLabels) {
    var row = el('div', 'ctx-item');
    var head = el('div', 'ctx-item-head');
    head.appendChild(ctxRoleTag(it, roleLabels));
    head.appendChild(elText('span', 'ctx-i', '#' + it.i));
    head.appendChild(elText('span', 'ctx-size', fmtCount(it.chars) + ' 字符' + (it.truncated ? '（已截断）' : '')));
    if (it.tool) { head.appendChild(elText('span', 'ctx-tool', it.tool)); }
    if (it.time > 0) { head.appendChild(elText('span', 'ctx-time', ctxFmtTime(it.time))); }
    row.appendChild(head);
    var body = elText('div', 'ctx-body', it.preview || '（空）');
    row.appendChild(body);
    var full = elText('pre', 'ctx-full', it.content || '（空）');
    full.style.display = 'none';
    row.appendChild(full);
    head.addEventListener('click', function () {
        var open = (full.style.display !== 'none');
        full.style.display = open ? 'none' : 'block';
        body.style.display = open ? 'block' : 'none';
        row.classList.toggle('open', !open);
    });
    return row;
}

/// tokens 视图——按字符规模降序；占比 = 该条字符 / 展示条目字符合计（估算口径，头行显式标注）
function ctxRenderTokens(list, items) {
    var total = 0;
    for (var i = 0; i < items.length; i = i + 1) {
        total = total + (items[i].chars || 0);
    }
    var sorted = items.slice().sort(function (a, b) { return (b.chars || 0) - (a.chars || 0); });
    for (var k = 0; k < sorted.length; k = k + 1) {
        var it = sorted[k];
        var pct = total > 0 ? (it.chars || 0) / total * 100 : 0;
        var row = el('div', 'ctx-item ctx-item-tok');
        var head = el('div', 'ctx-item-head');
        head.appendChild(ctxRoleTag(it, null));
        head.appendChild(elText('span', 'ctx-i', '#' + it.i));
        head.appendChild(elText('span', 'ctx-size', fmtCount(it.chars) + ' 字符'));
        if (it.tool) { head.appendChild(elText('span', 'ctx-tool', it.tool)); }
        head.appendChild(elText('span', 'ctx-pct', pct.toFixed(1) + '%'));
        row.appendChild(head);
        var bar = el('div', 'ctx-bar');
        var fill = el('div', 'ctx-bar-fill ctx-role-bg-' + (it.role || 'user'));
        fill.style.width = (pct < 0.4 ? 0.4 : pct) + '%';
        bar.appendChild(fill);
        row.appendChild(bar);
        list.appendChild(row);
    }
    if (items.length === 0) { list.textContent = '前文为空'; }
}

/// 角色标签——中文名缺失时回落原角色名
function ctxRoleTag(it, roleLabels) {
    var role = it.role || 'user';
    return elText('span', 'ctx-role ctx-role-' + role, (roleLabels && roleLabels[role]) ? roleLabels[role] : role);
}

/// 条目时刻——Unix 毫秒 → HH:mm:ss（本地时区）
function ctxFmtTime(ms) {
    var d = new Date(ms);
    var hh = d.getHours();
    var mm = d.getMinutes();
    var ss = d.getSeconds();
    return (hh < 10 ? '0' : '') + hh + ':' + (mm < 10 ? '0' : '') + mm + ':' + (ss < 10 ? '0' : '') + ss;
}

/// 接线——信息位委托点击（`[data-ctx]` 段）+ 弹层控件 + 关闭面；本件加载于页面尾部，DOM 已就绪
function ctxBind() {
    var info = document.getElementById('chatInfo');
    if (info) {
        info.addEventListener('click', function (ev) {
            var node = ev.target;
            while (node && node !== info) {
                if (node.getAttribute && node.getAttribute('data-ctx')) {
                    // 入口点击不冒泡到文档——否则刚开就被「点弹层外」误判关闭（判例同 note.js 的 wrap 排除）
                    ev.stopPropagation();
                    ctxOpen(node.getAttribute('data-ctx'));
                    return;
                }
                node = node.parentNode;
            }
        });
    }
    var refresh = document.getElementById('ctxRefresh');
    if (refresh) {
        refresh.addEventListener('click', ctxRefresh);
    }
    var modes = ['list', 'tokens', 'key', 'full'];
    for (var i = 0; i < modes.length; i = i + 1) {
        var tab = document.getElementById(ctxModeId(modes[i]));
        if (tab) {
            tab.addEventListener('click', ctxTabHandler(modes[i]));
        }
    }
    var pop = document.getElementById('ctxPopover');
    if (pop) {
        // 弹层内点击不冒泡到文档（避免被「点外部关闭」误判）
        pop.addEventListener('click', function (ev) { ev.stopPropagation(); });
        document.addEventListener('click', function (ev) {
            if (pop.style.display === 'none') { return; }
            if (pop.contains(ev.target)) { return; }
            ctxClose();
        });
    }
    document.addEventListener('keydown', function (ev) {
        if (ev.key === 'Escape' && pop && pop.style.display !== 'none') {
            ctxClose();
        }
    });
}

/// 页签点击处理件——闭包固定视图名（避免循环变量捕获）
function ctxTabHandler(mode) {
    return function () { ctxSwitch(mode); };
}

ctxBind();
