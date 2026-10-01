// CH4 外观层——panel-apis.js：LLM API 池 + QQ Bot 池管理（config 页签）——panel.js 拆分（2026-09-08 体量治理）
// 依赖：app.js + panel.js 先加载（全局状态已就位）；本文件承载 API/QQBot 两池的列表（行内编辑）+ 新建 + 删除
// 2026-10-01 行内编辑轮：行内「编辑」按钮撤除——可编辑列直接在格内改（配置 ID 只读）；未改动 / 清空 = 不改动（不发请求）
// 2026-10-01 默认列轮：★ 独立为最左「默认」列——紫星=当前默认 / 灰星=非默认；「设为默认 / 已是默认」随星同列（原在操作列）

// [段14] M3b LLM API 池管理（config 页签——行内编辑 + 新建 + 删除；key 掩码）
var apisTableBody = document.querySelector('#apisTable tbody');
var apisMsgEl = document.getElementById('apisMsg');

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

// 行内文本输入框——撑满单元格（列宽由 thead 控制）
function cellInput(value, placeholder) {
    var inp = document.createElement('input');
    inp.className = 'input-mini';
    inp.style.width = '100%';
    inp.value = value;
    inp.placeholder = placeholder;
    return inp;
}

function renderApiRow(api) {
    var tr = document.createElement('tr');
    // 原值对照面——未改动 / 清空判据；inputs 供整行提交取值
    var cells = {
        displayName: api.displayName || '',
        apiType: api.apiType || '',
        endpoint: api.endpoint || '',
        defaultModel: api.defaultModel || '',
        apiKey: api.apiKey || ''
    };
    var inputs = {};

    // 默认列（最左）——紫星=当前默认 / 灰星=非默认；状态与动作同格
    var tdDefault = document.createElement('td');
    tdDefault.style.whiteSpace = 'nowrap';
    var star = document.createElement('span');
    star.className = api.isDefault ? 'api-star-on' : 'api-star-off';
    star.textContent = '★';
    tdDefault.appendChild(star);
    if (api.isDefault) {
        var nowTag = document.createElement('span');
        nowTag.className = 'api-def-now';
        nowTag.textContent = '已是默认';
        tdDefault.appendChild(nowTag);
    } else {
        var defBtn = document.createElement('button');
        defBtn.textContent = '设为默认';
        defBtn.className = 'btn-mini accent';
        defBtn.style.marginLeft = '4px';
        defBtn.onclick = function () { setDefaultApi(api); };
        tdDefault.appendChild(defBtn);
    }
    tr.appendChild(tdDefault);

    var tdName = document.createElement('td');
    tdName.style.color = 'var(--ch-identity)';
    inputs.displayName = cellInput(cells.displayName, '名称');
    tdName.appendChild(inputs.displayName);
    tr.appendChild(tdName);

    var tdId = document.createElement('td');
    tdId.textContent = api.apiConfigId || '';
    tdId.style.color = 'var(--ch-fg-weak)';
    tdId.style.fontSize = '10px';
    tdId.style.wordBreak = 'break-all';
    tr.appendChild(tdId);

    var tdType = document.createElement('td');
    inputs.apiType = cellInput(cells.apiType, '类型（deepseek）');
    tdType.appendChild(inputs.apiType);
    tr.appendChild(tdType);

    var tdEndpoint = document.createElement('td');
    inputs.endpoint = cellInput(cells.endpoint, '端点 URL');
    tdEndpoint.appendChild(inputs.endpoint);
    tr.appendChild(tdEndpoint);

    var tdModel = document.createElement('td');
    inputs.defaultModel = cellInput(cells.defaultModel, '模型');
    tdModel.appendChild(inputs.defaultModel);
    tr.appendChild(tdModel);

    var tdKey = document.createElement('td');
    inputs.apiKey = cellInput(cells.apiKey, api.hasKey ? '留空=不改' : '（未配置）');
    tdKey.appendChild(inputs.apiKey);
    tr.appendChild(tdKey);

    // 行内编辑接线——未改动 / 清空都不提交（清空回落原值）
    wireInlineEdit(inputs.displayName, cells.displayName, function () { submitApiEdit(api, cells, inputs, '名称'); });
    wireInlineEdit(inputs.apiType, cells.apiType, function () { submitApiEdit(api, cells, inputs, '类型'); });
    wireInlineEdit(inputs.endpoint, cells.endpoint, function () { submitApiEdit(api, cells, inputs, '端点'); });
    wireInlineEdit(inputs.defaultModel, cells.defaultModel, function () { submitApiEdit(api, cells, inputs, '模型'); });
    wireInlineEdit(inputs.apiKey, cells.apiKey, function () { submitApiEdit(api, cells, inputs, 'Key'); });

    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.className = 'btn-mini danger';
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
            })
            .catch(function (e) {
                apisMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
            });
    };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    apisTableBody.appendChild(tr);
}

// 行内提交——整行字段一起送（后端按字段整体写入）；apiKey 未改动送空串（空=保留原 key）
function submitApiEdit(api, cells, inputs, label) {
    var keyVal = inputs.apiKey.value.trim();
    var payload = {
        apiConfigId: api.apiConfigId,
        displayName: inputs.displayName.value.trim(),
        apiType: inputs.apiType.value.trim(),
        endpoint: inputs.endpoint.value.trim(),
        defaultModel: inputs.defaultModel.value.trim(),
        apiKey: keyVal === cells.apiKey ? '' : keyVal
    };
    apisMsgEl.textContent = '保存中…（' + label + '）';
    fetch('/api/v1/llm-apis/edit', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apisMsgEl.textContent = d.ok ? ('已保存（' + label + '）') : '保存失败: ' + d.error;
            loadApis();
        })
        .catch(function (e) {
            apisMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

// 新建——底部表单（行内编辑后表单只承担新建；Key 可留空）
function apiSubmit() {
    var payload = {
        displayName: document.getElementById('apiNewName').value.trim(),
        apiType: document.getElementById('apiNewType').value.trim(),
        endpoint: document.getElementById('apiNewEndpoint').value.trim(),
        defaultModel: document.getElementById('apiNewModel').value.trim(),
        apiKey: document.getElementById('apiNewKey').value.trim()
    };
    fetch('/api/v1/llm-apis', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apisMsgEl.textContent = d.ok ? '已新建' : '失败: ' + d.error;
            if (d.ok) {
                document.getElementById('apiNewName').value = '';
                document.getElementById('apiNewType').value = '';
                document.getElementById('apiNewEndpoint').value = '';
                document.getElementById('apiNewModel').value = '';
                document.getElementById('apiNewKey').value = '';
                loadApis();
            }
        })
        .catch(function (e) {
            apisMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
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
        })
        .catch(function (e) {
            apisMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

document.getElementById('apiAddBtn').onclick = apiSubmit;
document.getElementById('apisRefresh').onclick = loadApis;
loadApis();

// [段14b] R2.3 QQ Bot 池管理（config 页签——行内编辑 + 新建 + 删除；secret 掩码；注册即建 WS 连接）
var qqbotsTableBody = document.querySelector('#qqbotsTable tbody');
var qqbotsMsgEl = document.getElementById('qqbotsMsg');

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
    var cells = {
        displayName: bot.displayName || '',
        appId: bot.appId || '',
        sandbox: bot.sandbox ? true : false,
        secret: bot.secret || ''
    };
    var inputs = {};

    var tdName = document.createElement('td');
    tdName.style.color = 'var(--ch-identity)';
    inputs.displayName = cellInput(cells.displayName, '名称');
    tdName.appendChild(inputs.displayName);
    tr.appendChild(tdName);

    var tdId = document.createElement('td');
    tdId.textContent = bot.qqBotId || '';
    tdId.style.color = 'var(--ch-fg-weak)';
    tdId.style.fontSize = '10px';
    tdId.style.wordBreak = 'break-all';
    tr.appendChild(tdId);

    var tdAppId = document.createElement('td');
    inputs.appId = cellInput(cells.appId, 'AppId');
    tdAppId.appendChild(inputs.appId);
    tr.appendChild(tdAppId);

    // 环境列——下拉直改（沙箱 / 正式）
    var tdSandbox = document.createElement('td');
    inputs.sandbox = document.createElement('select');
    inputs.sandbox.className = 'input-mini';
    inputs.sandbox.style.width = '100%';
    var optSandbox = document.createElement('option');
    optSandbox.value = 'true';
    optSandbox.textContent = '沙箱';
    var optProd = document.createElement('option');
    optProd.value = 'false';
    optProd.textContent = '正式';
    inputs.sandbox.appendChild(optSandbox);
    inputs.sandbox.appendChild(optProd);
    inputs.sandbox.value = cells.sandbox ? 'true' : 'false';
    inputs.sandbox.style.color = cells.sandbox ? 'var(--ch-warn)' : 'var(--ch-ok-weak)';
    tdSandbox.appendChild(inputs.sandbox);
    tr.appendChild(tdSandbox);

    var tdSecret = document.createElement('td');
    inputs.secret = cellInput(cells.secret, bot.hasSecret ? '留空=不改' : '（未配置）');
    tdSecret.appendChild(inputs.secret);
    tr.appendChild(tdSecret);

    wireInlineEdit(inputs.displayName, cells.displayName, function () { submitQqBotEdit(bot, cells, inputs, '名称'); });
    wireInlineEdit(inputs.appId, cells.appId, function () { submitQqBotEdit(bot, cells, inputs, 'AppId'); });
    wireInlineEdit(inputs.secret, cells.secret, function () { submitQqBotEdit(bot, cells, inputs, 'Secret'); });
    inputs.sandbox.addEventListener('change', function () {
        var sandbox = inputs.sandbox.value === 'true';
        if (sandbox === cells.sandbox) { return; }   // 未改动
        submitQqBotEdit(bot, cells, inputs, '环境');
    });

    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.className = 'btn-mini danger';
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
            })
            .catch(function (e) {
                qqbotsMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
            });
    };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    qqbotsTableBody.appendChild(tr);
}

// 行内提交——整行字段一起送；secret 未改动送空串（空=保留原 secret）；改完宿主即刷新 WS 连接
function submitQqBotEdit(bot, cells, inputs, label) {
    var secretVal = inputs.secret.value.trim();
    var payload = {
        qqBotId: bot.qqBotId,
        displayName: inputs.displayName.value.trim(),
        appId: inputs.appId.value.trim(),
        sandbox: inputs.sandbox.value === 'true',
        secret: secretVal === cells.secret ? '' : secretVal
    };
    qqbotsMsgEl.textContent = '保存中…（' + label + '）';
    fetch('/api/v1/qqbot-apis/edit', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            qqbotsMsgEl.textContent = d.ok ? ('已保存（' + label + '）') : '保存失败: ' + d.error;
            loadQqBots();
        })
        .catch(function (e) {
            qqbotsMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

// 新建——底部表单（行内编辑后表单只承担新建；Secret 可留空）
function qqBotSubmit() {
    var payload = {
        displayName: document.getElementById('qqNewName').value.trim(),
        appId: document.getElementById('qqNewAppId').value.trim(),
        sandbox: document.getElementById('qqNewSandbox').value === 'true',
        secret: document.getElementById('qqNewSecret').value.trim()
    };
    fetch('/api/v1/qqbot-apis', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            qqbotsMsgEl.textContent = d.ok ? '已新建' : '失败: ' + d.error;
            if (d.ok) {
                document.getElementById('qqNewName').value = '';
                document.getElementById('qqNewAppId').value = '';
                document.getElementById('qqNewSandbox').value = 'true';
                document.getElementById('qqNewSecret').value = '';
                loadQqBots();
            }
        })
        .catch(function (e) {
            qqbotsMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

document.getElementById('qqAddBtn').onclick = qqBotSubmit;
document.getElementById('qqbotsRefresh').onclick = loadQqBots;
loadQqBots();
