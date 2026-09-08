// CH4 外观层——panel-apis.js：LLM API 池 + QQ Bot 池管理（config 页签）——panel.js 拆分（2026-09-08 体量治理）
// 依赖：app.js + panel.js 先加载（全局状态已就位）；本文件只承载 API/QQBot 两池的列表/新建/编辑/删除
// 段14（M3b LLM API 池）+ 段14b（R2.3 QQ Bot 池）原样迁出，逻辑零改动

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
    var tdId = document.createElement('td');
    tdId.textContent = api.apiConfigId || '';
    tdId.style.color = '#6a6a6a';
    tdId.style.fontSize = '10px';
    tdId.style.wordBreak = 'break-all';
    tr.appendChild(tdId);
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

// [段14b] R2.3 QQ Bot 池管理（config 页签——列表/新建/编辑/删除；secret 掩码；注册即建 WS 连接）
var qqbotsTableBody = document.querySelector('#qqbotsTable tbody');
var qqbotsMsgEl = document.getElementById('qqbotsMsg');
var editingQqBotId = '';

function loadQqBots() {
    fetch('/api/v1/qqbot-apis')
        .then(function (r) { return r.json(); })
        .then(function (d) { renderQqBots(d.items || []); })
        .catch(function () { qqbotsMsgEl.textContent = 'QQ Bot 池加载失败——宿主未运行？'; });
}

function renderQqBots(items) {
    qqbotsTableBody.textContent = '';
    qqbotsMsgEl.textContent = items.length + ' 个 Bot';
    for (var i = 0; i < items.length; i++) {
        renderQqBotRow(items[i]);
    }
}

function renderQqBotRow(bot) {
    var tr = document.createElement('tr');
    var tdName = document.createElement('td');
    tdName.textContent = bot.displayName || '';
    tdName.style.color = '#c586c0';
    tr.appendChild(tdName);
    var tdId = document.createElement('td');
    tdId.textContent = bot.qqBotId || '';
    tdId.style.color = '#6a6a6a';
    tdId.style.fontSize = '10px';
    tdId.style.wordBreak = 'break-all';
    tr.appendChild(tdId);
    var tdAppId = document.createElement('td');
    tdAppId.textContent = bot.appId || '';
    tr.appendChild(tdAppId);
    var tdSandbox = document.createElement('td');
    tdSandbox.textContent = bot.sandbox ? '沙箱' : '正式';
    tdSandbox.style.color = bot.sandbox ? '#dcdcaa' : '#6a9955';
    tr.appendChild(tdSandbox);
    var tdSecret = document.createElement('td');
    tdSecret.textContent = bot.hasSecret ? bot.secret : '（未配置）';
    tdSecret.style.color = bot.hasSecret ? '#6a9955' : '#6a6a6a';
    tr.appendChild(tdSecret);
    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    var editBtn = document.createElement('button');
    editBtn.textContent = '编辑';
    editBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
    editBtn.onclick = function () { startQqBotEdit(bot); };
    tdOp.appendChild(editBtn);
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px';
    delBtn.onclick = function () {
        if (!confirm('删除 QQ Bot「' + bot.displayName + '」？绑定它的猫将回退未绑定。')) { return; }
        fetch('/api/v1/qqbot-apis/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ qqBotId: bot.qqBotId })
        })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                qqbotsMsgEl.textContent = d.ok ? '已删除' : '删除失败: ' + d.error;
                loadQqBots();
            });
    };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    qqbotsTableBody.appendChild(tr);
}

function startQqBotEdit(bot) {
    editingQqBotId = bot.qqBotId;
    document.getElementById('qqNewName').value = bot.displayName || '';
    document.getElementById('qqNewAppId').value = bot.appId || '';
    document.getElementById('qqNewSandbox').value = bot.sandbox ? 'true' : 'false';
    document.getElementById('qqNewSecret').value = '';
    document.getElementById('qqAddBtn').textContent = '保存';
    document.getElementById('qqCancelEdit').style.display = '';
    qqbotsMsgEl.textContent = '编辑中: ' + bot.displayName + '（Secret 留空=保留原 Secret）';
}

function cancelQqBotEdit() {
    editingQqBotId = '';
    document.getElementById('qqNewName').value = '';
    document.getElementById('qqNewAppId').value = '';
    document.getElementById('qqNewSandbox').value = 'true';
    document.getElementById('qqNewSecret').value = '';
    document.getElementById('qqAddBtn').textContent = '新建';
    document.getElementById('qqCancelEdit').style.display = 'none';
    qqbotsMsgEl.textContent = '';
}

function qqBotSubmit() {
    var payload = {
        displayName: document.getElementById('qqNewName').value.trim(),
        appId: document.getElementById('qqNewAppId').value.trim(),
        sandbox: document.getElementById('qqNewSandbox').value === 'true',
        secret: document.getElementById('qqNewSecret').value.trim()
    };
    var url = '/api/v1/qqbot-apis';
    if (editingQqBotId.length > 0) {
        url = '/api/v1/qqbot-apis/edit';
        payload.qqBotId = editingQqBotId;
    }
    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            qqbotsMsgEl.textContent = d.ok ? (editingQqBotId.length > 0 ? '已保存' : '已新建') : '失败: ' + d.error;
            cancelQqBotEdit();
            loadQqBots();
        })
        .catch(function (e) {
            qqbotsMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

document.getElementById('qqAddBtn').onclick = qqBotSubmit;
document.getElementById('qqbotsRefresh').onclick = loadQqBots;
document.getElementById('qqCancelEdit').onclick = cancelQqBotEdit;
loadQqBots();
