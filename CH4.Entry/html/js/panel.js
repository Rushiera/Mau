// CH4 外观层 v2——panel.js：多猫管理区 + LLM API 池 + 每猫配置弹层（2026-08-25 拆分自 index.html）
// 依赖：app.js 先加载（全局状态与 SSE 已就位）

// [段13] 多猫管理区（P9.3d）——GET /api/v1/cats 列表 + POST command cat.* 指令族
var catsTableBody = document.querySelector('#catsTable tbody');
var catsMsgEl = document.getElementById('catsMsg');
var catNewInput = document.getElementById('catNewName');

// 猫列表加载——渲染表格（名称/ID/状态/端口/操作按钮）
function loadCats() {
    fetch('/api/v1/cats')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            catsTableBody.textContent = '';
            var cats = d.cats || [];
            if (cats.length === 0) {
                catsMsgEl.textContent = '暂无猫——输入显示名新建';
                return;
            }
            catsMsgEl.textContent = cats.length + ' 只猫';
            for (var i = 0; i < cats.length; i++) {
                renderCatRow(cats[i]);
            }
        })
        .catch(function () {
            catsMsgEl.textContent = '列表加载失败——宿主未运行？';
        });
}

// 单猫行渲染——操作按钮按状态启用（启动/停止互斥）
function renderCatRow(cat) {
    var tr = document.createElement('tr');
    var tdName = document.createElement('td');
    tdName.textContent = cat.name || '';
    tdName.style.color = '#c586c0';
    tr.appendChild(tdName);
    var tdId = document.createElement('td');
    tdId.textContent = cat.id || '';
    tdId.style.color = '#6a6a6a';
    tr.appendChild(tdId);
    var tdState = document.createElement('td');
    if (cat.running) {
        tdState.textContent = '运行中';
        tdState.style.color = '#4ec9b0';
    } else {
        tdState.textContent = '静默';
        tdState.style.color = '#6a6a6a';
    }
    tr.appendChild(tdState);
    var tdPort = document.createElement('td');
    tdPort.textContent = cat.running ? (':' + cat.port) : '-';
    tr.appendChild(tdPort);
    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    if (cat.running) {
        var openBtn = document.createElement('button');
        openBtn.textContent = '打开';
        openBtn.style.cssText = 'background:#0e639c;border:none;color:#fff;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        openBtn.onclick = function () { window.open('http://127.0.0.1:' + cat.port + '/', '_blank'); };
        tdOp.appendChild(openBtn);
        var stopBtn = document.createElement('button');
        stopBtn.textContent = '停止';
        stopBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        stopBtn.onclick = function () { catAction('cat.stop ' + cat.id); };
        tdOp.appendChild(stopBtn);
    } else {
        var startBtn = document.createElement('button');
        startBtn.textContent = '启动';
        startBtn.style.cssText = 'background:#0e639c;border:none;color:#fff;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        startBtn.onclick = function () { catAction('cat.start ' + cat.id); };
        tdOp.appendChild(startBtn);
    }
    var cfgBtn = document.createElement('button');
    cfgBtn.textContent = '配置';
    cfgBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
    cfgBtn.onclick = function () { openCatCfg(cat.id, cat.name); };
    tdOp.appendChild(cfgBtn);
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px';
    delBtn.onclick = function () { catAction('cat.delete ' + cat.id); };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    catsTableBody.appendChild(tr);
}

// cat.* 指令投递——POST command 后延迟刷新（指令经 HTTP 线程入队主线程泵，异步生效）
function catAction(cmd) {
    fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: cmd })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            catsMsgEl.textContent = '回执: ' + cmd + ' → ok=' + d.ok + (d.error ? ' error=' + d.error : '');
            setTimeout(loadCats, 400);
        })
        .catch(function (err) {
            catsMsgEl.textContent = '请求失败: ' + err;
        });
}

// 新建猫——cat.new <显示名>（空名拒绝）
function catNew() {
    var name = catNewInput.value.trim();
    if (name.length === 0) {
        catsMsgEl.textContent = '显示名不能为空';
        return;
    }
    catNewInput.value = '';
    catAction('cat.new ' + name);
}

// 多猫区按钮绑定 + 列表初始化
document.getElementById('catNewBtn').addEventListener('click', catNew);
catNewInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { catNew(); }
});
loadCats();

// [段14] M3b LLM API 池管理（config 页签——列表/新建/编辑/删除；key 掩码）
var apisTableBody = document.querySelector('#apisTable tbody');
var apisMsgEl = document.getElementById('apisMsg');
var editingApiId = '';

function loadApis() {
    fetch('/api/v1/llm-apis')
        .then(function (r) { return r.json(); })
        .then(function (d) { renderApis(d.items || []); })
        .catch(function () { apisMsgEl.textContent = 'API 池加载失败——宿主未运行？'; });
}

function renderApis(items) {
    apisTableBody.textContent = '';
    apisMsgEl.textContent = items.length + ' 组配置';
    for (var i = 0; i < items.length; i++) {
        renderApiRow(items[i]);
    }
}

function renderApiRow(api) {
    var tr = document.createElement('tr');
    var tdName = document.createElement('td');
    tdName.textContent = api.displayName || '';
    tdName.style.color = '#c586c0';
    if (api.isDefault) { tdName.textContent = '★ ' + tdName.textContent; }
    tr.appendChild(tdName);
    var tdType = document.createElement('td');
    tdType.textContent = api.apiType || '';
    tr.appendChild(tdType);
    var tdEndpoint = document.createElement('td');
    tdEndpoint.textContent = api.endpoint || '';
    tdEndpoint.style.wordBreak = 'break-all';
    tr.appendChild(tdEndpoint);
    var tdModel = document.createElement('td');
    tdModel.textContent = api.defaultModel || '';
    tr.appendChild(tdModel);
    var tdKey = document.createElement('td');
    tdKey.textContent = api.hasKey ? api.apiKey : '（未配置）';
    tdKey.style.color = api.hasKey ? '#6a9955' : '#6a6a6a';
    tr.appendChild(tdKey);
    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    if (!api.isDefault) {
        var defBtn = document.createElement('button');
        defBtn.textContent = '设为默认';
        defBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#dcdcaa;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        defBtn.onclick = function () { setDefaultApi(api); };
        tdOp.appendChild(defBtn);
    }
    var editBtn = document.createElement('button');
    editBtn.textContent = '编辑';
    editBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
    editBtn.onclick = function () { startApiEdit(api); };
    tdOp.appendChild(editBtn);
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px';
    delBtn.onclick = function () {
        if (!confirm('删除 API 配置「' + api.displayName + '」？引用它的猫将回退空配置。')) { return; }
        fetch('/api/v1/llm-apis/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiConfigId: api.apiConfigId })
        })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                apisMsgEl.textContent = d.ok ? '已删除' : '删除失败: ' + d.error;
                loadApis();
            });
    };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    apisTableBody.appendChild(tr);
}

function startApiEdit(api) {
    editingApiId = api.apiConfigId;
    document.getElementById('apiNewName').value = api.displayName || '';
    document.getElementById('apiNewType').value = api.apiType || '';
    document.getElementById('apiNewEndpoint').value = api.endpoint || '';
    document.getElementById('apiNewModel').value = api.defaultModel || '';
    document.getElementById('apiNewKey').value = '';
    document.getElementById('apiAddBtn').textContent = '保存';
    document.getElementById('apiCancelEdit').style.display = '';
    apisMsgEl.textContent = '编辑中: ' + api.displayName + '（Key 留空=保留原 Key）';
}

function cancelApiEdit() {
    editingApiId = '';
    document.getElementById('apiNewName').value = '';
    document.getElementById('apiNewType').value = '';
    document.getElementById('apiNewEndpoint').value = '';
    document.getElementById('apiNewModel').value = '';
    document.getElementById('apiNewKey').value = '';
    document.getElementById('apiAddBtn').textContent = '新建';
    document.getElementById('apiCancelEdit').style.display = 'none';
    apisMsgEl.textContent = '';
}

function apiSubmit() {
    var payload = {
        displayName: document.getElementById('apiNewName').value.trim(),
        apiType: document.getElementById('apiNewType').value.trim(),
        endpoint: document.getElementById('apiNewEndpoint').value.trim(),
        defaultModel: document.getElementById('apiNewModel').value.trim(),
        apiKey: document.getElementById('apiNewKey').value.trim()
    };
    var url = '/api/v1/llm-apis';
    if (editingApiId.length > 0) {
        url = '/api/v1/llm-apis/edit';
        payload.apiConfigId = editingApiId;
    }
    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apisMsgEl.textContent = d.ok ? (editingApiId.length > 0 ? '已保存' : '已新建') : '失败: ' + d.error;
            cancelApiEdit();
            loadApis();
        });
}

// 设为默认——POST /api/v1/llm-apis/default（默认端点：QuickCat 语料面与未显式配置的猫固定走它；切换立即生效）
function setDefaultApi(api) {
    fetch('/api/v1/llm-apis/default', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiConfigId: api.apiConfigId })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apisMsgEl.textContent = d.ok ? '默认端点已切换: ' + api.displayName : '失败: ' + d.error;
            loadApis();
        });
}

document.getElementById('apiAddBtn').onclick = apiSubmit;
document.getElementById('apisRefresh').onclick = loadApis;
document.getElementById('apiCancelEdit').onclick = cancelApiEdit;
loadApis();

// [段15] M3c 每猫配置弹层——API 下拉/人设/工具勾选/前文 List
var catCfgModal = document.getElementById('catCfgModal');
var catCfgTarget = '';
var catCfgAllTools = [];
var catCfgInjectList = [];

function openCatCfg(catId, name) {
    catCfgTarget = catId;
    document.getElementById('catCfgTitle').textContent = name + ' (' + catId + ')';
    document.getElementById('catCfgMsg').textContent = '';
    fetch('/api/v1/cat-config?cat=' + encodeURIComponent(catId))
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok) {
                document.getElementById('catCfgMsg').textContent = '读取失败: ' + (d.error || '');
                catCfgModal.style.display = 'flex';
                return;
            }
            catCfgAllTools = d.allToolNames || [];
            renderApiOptions(d.apiOptions || [], d.apiConfigId || '');
            document.getElementById('catCfgPersona').value = d.persona || '';
            renderToolChecks(d.toolNames || '');
            renderInjectList(d.injectList || []);
            catCfgModal.style.display = 'flex';
        })
        .catch(function () {
            document.getElementById('catCfgMsg').textContent = '读取失败——宿主未运行？';
            catCfgModal.style.display = 'flex';
        });
}

function renderApiOptions(options, current) {
    var sel = document.getElementById('catCfgApi');
    sel.textContent = '';
    for (var i = 0; i < options.length; i++) {
        var opt = document.createElement('option');
        opt.value = options[i].apiConfigId;
        opt.textContent = options[i].displayName + ' (' + options[i].apiConfigId.substring(0, 8) + '…)';
        if (options[i].apiConfigId === current) { opt.selected = true; }
        sel.appendChild(opt);
    }
}

function renderToolChecks(toolNames) {
    var box = document.getElementById('catCfgTools');
    box.textContent = '';
    var checked = {};
    if (toolNames && toolNames.length > 0 && toolNames !== '*') {
        var parts = toolNames.split(',');
        for (var j = 0; j < parts.length; j++) {
            var n = parts[j].trim();
            if (n.length > 0) { checked[n] = true; }
        }
    }
    for (var k = 0; k < catCfgAllTools.length; k++) {
        (function (toolName) {
            var label = document.createElement('label');
            label.style.cssText = 'display:flex;align-items:center;gap:4px;background:#242424;border:1px solid #2a2a2a;border-radius:4px;padding:3px 8px;font-size:11px;color:#c8c8c8;cursor:pointer';
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = checked[toolName] === true;
            label.appendChild(cb);
            label.appendChild(document.createTextNode(toolName));
            box.appendChild(label);
        })(catCfgAllTools[k]);
    }
}

function renderInjectList(list) {
    catCfgInjectList = list.slice();
    var box = document.getElementById('catCfgInject');
    box.textContent = '';
    for (var i = 0; i < catCfgInjectList.length; i++) {
        (function (idx) {
            var row = document.createElement('div');
            row.style.cssText = 'display:flex;gap:6px;align-items:center;margin-top:3px';
            var txt = document.createElement('span');
            txt.textContent = catCfgInjectList[idx];
            txt.style.cssText = 'flex:1;color:#6a9955;font-size:11px;word-break:break-all';
            row.appendChild(txt);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px';
            rm.onclick = function () {
                catCfgInjectList.splice(idx, 1);
                renderInjectList(catCfgInjectList);
            };
            row.appendChild(rm);
            box.appendChild(row);
        })(i);
    }
}

function saveCatCfg() {
    var toolNames = [];
    var boxes = document.querySelectorAll('#catCfgTools input[type=checkbox]');
    for (var i = 0; i < boxes.length; i++) {
        if (boxes[i].checked) { toolNames.push(catCfgAllTools[i]); }
    }
    var payload = {
        cat: catCfgTarget,
        apiConfigId: document.getElementById('catCfgApi').value,
        persona: document.getElementById('catCfgPersona').value,
        toolNames: toolNames.join(','),
        injectList: catCfgInjectList
    };
    fetch('/api/v1/cat-config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            document.getElementById('catCfgMsg').textContent = d.ok ? '已保存——API 即时生效；人设/工具/前文 List 新会话生效' : '保存失败: ' + d.error;
        });
}

document.getElementById('catCfgSave').onclick = saveCatCfg;
document.getElementById('catCfgCancel').onclick = function () { catCfgModal.style.display = 'none'; };
document.getElementById('catCfgClose').onclick = function () { catCfgModal.style.display = 'none'; };
document.getElementById('catCfgInjectAddBtn').onclick = function () {
    var input = document.getElementById('catCfgInjectAdd');
    var val = input.value.trim();
    if (val.length === 0) { return; }
    catCfgInjectList.push(val);
    input.value = '';
    renderInjectList(catCfgInjectList);
};
// 默认猫配置入口——对话区按钮（cat=majordomo）
document.getElementById('chatCfg').onclick = function () { openCatCfg('majordomo', 'majordomo'); };
