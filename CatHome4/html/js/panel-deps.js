// CH4 外观层——panel-deps.js：依赖检测区（A253 5b · design-ch4-pytools.md §七）
// 依赖：app.js 先加载（fullSnapshot 与 SSE 已就位）；渲染入口 renderDeps(deps) 由 app.js applySnapshot 调用。
// 数据源：快照 deps 段（后端统一探测快照——D1 决策）；手动刷新走 POST /api/v1/deps/refresh
//         （重置全部条目探测时刻 → 下一轮快照全量重探，含「启动一次」档）。

var depsListEl = document.getElementById('depsList');
var depsInfoEl = document.getElementById('depsInfo');
var depsRefreshBtn = document.getElementById('depsRefresh');

// 级别 → 分组标题（顺序即展示序；与后端 DependencyProbe.LevelOrder 一致）
var DEPS_LEVELS = [
    { key: 'required', title: '运行必需' },
    { key: 'optional', title: '可选功能' },
    { key: 'dev', title: '开发辅助' }
];

// 状态 → 展示（三态分明：缺失 / 不可用报红；未配置属正常态——功能未启用不报红）
var DEPS_STATES = {
    ok: { text: '可用', color: 'var(--ch-ok)' },
    missing: { text: '缺失', color: 'var(--ch-err)' },
    unusable: { text: '不可用', color: 'var(--ch-err)' },
    unconfigured: { text: '未配置', color: 'var(--ch-fg-faint)' }
};

// 结构签名——同构时不重排 DOM（稳态零变动；detail 变化才重建——对齐 A225f 增量更新纪律）
var lastDepsSig = '';

// 渲染依赖列表——HTTP 初始快照与 SSE patch 共用同一路径（app.js applySnapshot → renderDeps）
function renderDeps(deps) {
    if (!depsListEl) { return; }
    var sigParts = [];
    for (var i = 0; i < deps.length; i = i + 1) {
        sigParts.push(deps[i].level + '|' + deps[i].name + '|' + deps[i].state + '|' + deps[i].detail);
    }
    var sig = sigParts.join('\u0001');
    if (sig === lastDepsSig) { return; }
    lastDepsSig = sig;
    depsListEl.textContent = '';
    var total = 0;
    var badCount = 0;
    for (var g = 0; g < DEPS_LEVELS.length; g = g + 1) {
        var level = DEPS_LEVELS[g];
        var rows = [];
        for (var j = 0; j < deps.length; j = j + 1) {
            if (deps[j].level === level.key) { rows.push(deps[j]); }
        }
        if (rows.length === 0) { continue; }
        var head = document.createElement('h2');
        head.textContent = level.title;
        depsListEl.appendChild(head);
        for (var k = 0; k < rows.length; k = k + 1) {
            total = total + 1;
            var st = DEPS_STATES[rows[k].state] || { text: rows[k].state, color: 'var(--ch-fg-faint)' };
            if (rows[k].state === 'missing' || rows[k].state === 'unusable') { badCount = badCount + 1; }
            depsListEl.appendChild(buildDepRow(rows[k], st));
        }
    }
    if (depsInfoEl) {
        depsInfoEl.textContent = '外部依赖与自举链——共 ' + total + ' 项' + (badCount > 0 ? ('，异常 ' + badCount + ' 项') : '，全部就绪');
    }
}

// 建一行——状态点 + 名称 + 状态标签 + 详情（详情超长省略，悬停看全文）
function buildDepRow(item, st) {
    var row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:7px 12px;border-bottom:1px solid var(--ch-line-faint)';
    var dot = document.createElement('span');
    dot.style.cssText = 'width:8px;height:8px;border-radius:50%;flex:0 0 auto;background:' + st.color;
    row.appendChild(dot);
    var name = document.createElement('span');
    name.textContent = item.name;
    name.style.cssText = 'flex:0 0 150px;color:var(--ch-fg-strong)';
    row.appendChild(name);
    var badge = document.createElement('span');
    badge.textContent = st.text;
    badge.style.cssText = 'flex:0 0 56px;font-size:var(--ch-fs-tag);color:' + st.color;
    row.appendChild(badge);
    var detail = document.createElement('span');
    detail.textContent = item.detail || '';
    detail.title = item.detail || '';
    detail.style.cssText = 'flex:1;min-width:0;color:var(--ch-fg-weak);font-size:var(--ch-fs-tag);overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
    row.appendChild(detail);
    return row;
}

// 手动刷新——重置后端探测时刻（下一轮快照全量重探；≤1 秒内 patch 推回）
if (depsRefreshBtn) {
    depsRefreshBtn.addEventListener('click', function () {
        depsRefreshBtn.disabled = true;
        if (depsInfoEl) { depsInfoEl.textContent = '重新探测中…'; }
        fetch('/api/v1/deps/refresh', { method: 'POST' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                lastDepsSig = '';   // 强制下一帧重建（即便 detail 未变也给出重探反馈）
                if (depsInfoEl) { depsInfoEl.textContent = '已触发重探——快照刷新中（≤1 秒）'; }
            })
            .catch(function () {
                if (depsInfoEl) { depsInfoEl.textContent = '刷新失败——宿主未运行？'; }
            })
            .then(function () { depsRefreshBtn.disabled = false; });
    });
}

// ── LLM 端点池探活（A253 扩展）——手动触发真实请求，逐个报可用状态 ──
// 数据源：POST /api/v1/llm/probe（后端 LlmEndpointProbe.ProbeAsync——真实链路：端点 URL / Bearer / 网关头）
// 常态不请求（成本）——只有点「发送探针」才发；结果留存于本页，刷新即清。
var llmProbeBtn = document.getElementById('llmProbeBtn');
var llmProbeInfo = document.getElementById('llmProbeInfo');
var llmProbeList = document.getElementById('llmProbeList');

// 状态 → 展示（ok 可用 / nokey 未配 Key（不报红，属未启用）/ fail 失败）
var LLM_STATES = {
    ok: { text: '可用', color: 'var(--ch-ok)' },
    nokey: { text: '未配 Key', color: 'var(--ch-fg-faint)' },
    fail: { text: '失败', color: 'var(--ch-err)' }
};

// 渲染探活结果——逐端点一行（状态点 + 名称 + 模型 + 状态 + 延迟 + 详情）
function renderLlmProbe(items) {
    if (!llmProbeList) { return; }
    llmProbeList.textContent = '';
    var okCount = 0;
    for (var i = 0; i < items.length; i = i + 1) {
        var item = items[i];
        var st = LLM_STATES[item.state] || { text: item.state, color: 'var(--ch-fg-faint)' };
        if (item.state === 'ok') { okCount = okCount + 1; }
        llmProbeList.appendChild(buildLlmRow(item, st));
    }
    if (llmProbeInfo) {
        llmProbeInfo.textContent = '探活完成——' + items.length + ' 个端点，可用 ' + okCount + ' 个';
    }
}

// 建一行——状态点 + 名称 + 模型 + 状态 + 延迟 + 详情（详情超长省略，悬停看全文）
function buildLlmRow(item, st) {
    var row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:7px 12px;border-bottom:1px solid var(--ch-line-faint)';
    var dot = document.createElement('span');
    dot.style.cssText = 'width:8px;height:8px;border-radius:50%;flex:0 0 auto;background:' + st.color;
    row.appendChild(dot);
    var name = document.createElement('span');
    name.textContent = item.name || '(未命名)';
    name.style.cssText = 'flex:0 0 150px;color:var(--ch-fg-strong)';
    row.appendChild(name);
    var model = document.createElement('span');
    model.textContent = item.model || '';
    model.style.cssText = 'flex:0 0 170px;color:var(--ch-fg-weak);font-size:var(--ch-fs-tag);overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
    row.appendChild(model);
    var badge = document.createElement('span');
    badge.textContent = st.text;
    badge.style.cssText = 'flex:0 0 70px;font-size:var(--ch-fs-tag);color:' + st.color;
    row.appendChild(badge);
    var ms = document.createElement('span');
    ms.textContent = item.ms > 0 ? (item.ms + ' ms') : '';
    ms.style.cssText = 'flex:0 0 70px;color:var(--ch-fg-faint);font-size:var(--ch-fs-tag)';
    row.appendChild(ms);
    var detail = document.createElement('span');
    detail.textContent = item.detail || '';
    detail.title = (item.endpoint || '') + '\n' + (item.detail || '');
    detail.style.cssText = 'flex:1;min-width:0;color:var(--ch-fg-weak);font-size:var(--ch-fs-tag);overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
    row.appendChild(detail);
    return row;
}

// 发送探针——真实请求（可能数秒：逐个端点串行）；完成后列可用状态
if (llmProbeBtn) {
    llmProbeBtn.addEventListener('click', function () {
        llmProbeBtn.disabled = true;
        if (llmProbeInfo) { llmProbeInfo.textContent = '探活中——逐个端点发最小请求…'; }
        if (llmProbeList) { llmProbeList.textContent = ''; }
        fetch('/api/v1/llm/probe', { method: 'POST' })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                if (!d || d.ok !== true) {
                    if (llmProbeInfo) { llmProbeInfo.textContent = '探活失败：' + ((d && d.error) || '未知错误'); }
                    return;
                }
                renderLlmProbe(d.items || []);
            })
            .catch(function () {
                if (llmProbeInfo) { llmProbeInfo.textContent = '探活请求失败——宿主未运行？'; }
            })
            .then(function () { llmProbeBtn.disabled = false; });
    });
}
