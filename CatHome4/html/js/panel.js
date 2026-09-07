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
            // F2.1 前端唯一化——special 猫（Majordomo）置顶显示（固化入口；前端不视为多猫——零差异渲染靠 special 标记）
            var specials = [];
            var normals = [];
            for (var i = 0; i < cats.length; i++) {
                if (cats[i].special) { specials.push(cats[i]); } else { normals.push(cats[i]); }
            }
            for (var s = 0; s < specials.length; s++) { renderCatRow(specials[s]); }
            for (var n = 0; n < normals.length; n++) { renderCatRow(normals[n]); }
        })
        .catch(function () {
            catsMsgEl.textContent = '列表加载失败——宿主未运行？';
        });
}

// 单猫行渲染——操作按钮按状态启用（启动/停止互斥）；special（Majordomo）强制自启 + 无停止/删除
function renderCatRow(cat) {
    var tr = document.createElement('tr');
    var tdName = document.createElement('td');
    if (cat.special) {
        tdName.textContent = '★ ' + (cat.name || '');
    } else {
        tdName.textContent = cat.name || '';
    }
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
        openBtn.textContent = '打开对话';
        openBtn.style.cssText = 'background:#0e639c;border:none;color:#fff;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        openBtn.onclick = function () { window.open('http://127.0.0.1:' + cat.port + '/', '_blank'); };
        tdOp.appendChild(openBtn);
        if (!cat.special) {
            var stopBtn = document.createElement('button');
            stopBtn.textContent = '停止';
            stopBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
            stopBtn.onclick = function () { catAction('cat.stop ' + cat.id); };
            tdOp.appendChild(stopBtn);
        }
    } else {
        var startBtn = document.createElement('button');
        startBtn.textContent = '启动';
        startBtn.style.cssText = 'background:#0e639c;border:none;color:#fff;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        startBtn.onclick = function () { catAction('cat.start ' + cat.id); };
        tdOp.appendChild(startBtn);
    }
    if (cat.special) {
        // Majordomo 特殊会话——无停止/删除，保留配置入口（openCatCfg 同多猫）
        var cfgBtn2 = document.createElement('button');
        cfgBtn2.textContent = '配置';
        cfgBtn2.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        cfgBtn2.onclick = function () { openCatCfg(cat.id, cat.name); };
        tdOp.appendChild(cfgBtn2);
    } else {
        var cfgBtn = document.createElement('button');
        cfgBtn.textContent = '配置';
        cfgBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
        cfgBtn.onclick = function () { openCatCfg(cat.id, cat.name); };
        tdOp.appendChild(cfgBtn);
        var delBtn = document.createElement('button');
        delBtn.textContent = '删除';
        delBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:3px 8px;cursor:pointer;font-family:inherit;font-size:11px';
        delBtn.onclick = function () {
            // 删除确认——猫销毁不可逆：配置（persona/工具面/API/注入/白名单）+ 前文全部丢失
            if (!window.confirm('删除猫「' + cat.name + '」？删除后该猫的所有配置与前文都将丢失（不可恢复）。')) { return; }
            catAction('cat.delete ' + cat.id);
        };
        tdOp.appendChild(delBtn);
    }
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
            catCfgAllTools = d.allTools || d.allToolNames || [];
            renderApiOptions(d.apiOptions || [], d.apiConfigId || '');
            renderQqBotOptions(d.qqbotOptions || [], d.qqbotId || '');
            document.getElementById('catCfgQqEnable').checked = !!d.qqbotEnable;
            document.getElementById('catCfgPersona').value = d.persona || '';
            renderToolChecks(d.toolNames || '');
            renderInjectList(d.injectList || []);
            // M4e 猫级白名单——启用根勾选（workspace 强制常驻不可取消）
            renderRootChecks(d.allRoots || [], d.enabledRoots || []);
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

// R2.3 QQ Bot 下拉——空选项=未绑定；多猫可绑同一 Bot
function renderQqBotOptions(options, current) {
    var sel = document.getElementById('catCfgQqBot');
    sel.textContent = '';
    var none = document.createElement('option');
    none.value = '';
    none.textContent = '（未绑定）';
    if (!current || current.length === 0) { none.selected = true; }
    sel.appendChild(none);
    for (var i = 0; i < options.length; i++) {
        var opt = document.createElement('option');
        opt.value = options[i].qqBotId;
        opt.textContent = options[i].displayName + ' (' + options[i].qqBotId.substring(0, 8) + '…)';
        if (options[i].qqBotId === current) { opt.selected = true; }
        sel.appendChild(opt);
    }
}

function parseToolChecked(toolNames) {
    var checked = {};
    if (toolNames && toolNames.length > 0 && toolNames !== '*') {
        var parts = toolNames.split(',');
        for (var j = 0; j < parts.length; j++) {
            var n = parts[j].trim();
            if (n.length > 0) { checked[n] = true; }
        }
    }
    return checked;
}

// 通用分组勾选渲染——优先按工具池 group 字段分组（allTools: [{name,group}]——design-ch4-tools-pool §六）；
// 兼容纯名数组（allToolNames: string[]）回退 - 前缀分组（text-*/mau-* 视为一组；无前缀归 other）；组头开关一键全组
function renderGroupedChecks(boxId, allTools, checked) {
    var box = document.getElementById(boxId);
    box.textContent = '';
    var groups = {};
    var order = [];
    for (var k = 0; k < allTools.length; k++) {
        var item = allTools[k];
        var toolName = typeof item === 'string' ? item : (item && item.name) || '';
        if (toolName.length === 0) { continue; }
        var group;
        if (typeof item === 'object' && item.group) {
            group = item.group;               // 工具池组别（TextCat/PsCat/...）——统一接口
        } else {
            var dash = toolName.indexOf('-');
            group = dash > 0 ? toolName.substring(0, dash) : 'other';  // 回退前缀
        }
        if (!groups[group]) { groups[group] = []; order.push(group); }
        groups[group].push(toolName);
    }
    for (var g = 0; g < order.length; g++) {
        (function (groupName, tools) {
            var head = document.createElement('label');
            head.style.cssText = 'display:flex;align-items:center;gap:4px;width:100%;background:#2d2d2d;border:1px solid #3a3a3a;border-radius:4px;padding:3px 8px;font-size:11px;color:#dcdcaa;cursor:pointer;font-weight:bold';
            var hcb = document.createElement('input');
            hcb.type = 'checkbox';
            head.appendChild(hcb);
            head.appendChild(document.createTextNode(groupName + '-*（' + tools.length + '）'));
            box.appendChild(head);
            var groupBox = document.createElement('div');
            groupBox.style.cssText = 'display:flex;flex-wrap:wrap;gap:6px;margin:4px 0 8px 12px;width:100%';
            var cbs = [];
            for (var t = 0; t < tools.length; t++) {
                (function (toolName) {
                    var label = document.createElement('label');
                    label.style.cssText = 'display:flex;align-items:center;gap:4px;background:#242424;border:1px solid #2a2a2a;border-radius:4px;padding:3px 8px;font-size:11px;color:#c8c8c8;cursor:pointer';
                    var cb = document.createElement('input');
                    cb.type = 'checkbox';
                    cb.className = 'tool-cb';
                    cb.setAttribute('data-tool', toolName);
                    cb.checked = checked[toolName] === true;
                    cbs.push(cb);
                    label.appendChild(cb);
                    label.appendChild(document.createTextNode(toolName));
                    groupBox.appendChild(label);
                })(tools[t]);
            }
            box.appendChild(groupBox);
            // 组开关——全选/全不选；组内变化回写半选态
            function syncHead() {
                var on = 0;
                for (var i = 0; i < cbs.length; i++) { if (cbs[i].checked) { on = on + 1; } }
                hcb.checked = (on === cbs.length);
                hcb.indeterminate = (on > 0 && on < cbs.length);
            }
            hcb.addEventListener('change', function () {
                for (var i = 0; i < cbs.length; i++) { cbs[i].checked = hcb.checked; }
            });
            for (var i2 = 0; i2 < cbs.length; i2++) {
                cbs[i2].addEventListener('change', syncHead);
            }
            syncHead();
        })(order[g], groups[order[g]]);
    }
}

// 通用勾选收集——data-tool 属性直读（不依赖渲染顺序）
function collectChecked(boxId) {
    var names = [];
    var boxes = document.querySelectorAll('#' + boxId + ' input[type=checkbox].tool-cb');
    for (var i = 0; i < boxes.length; i++) {
        if (boxes[i].checked) { names.push(boxes[i].getAttribute('data-tool')); }
    }
    return names;
}

function renderToolChecks(toolNames) {
    renderGroupedChecks('catCfgTools', catCfgAllTools, parseToolChecked(toolNames));
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
    var toolNames = collectChecked('catCfgTools');
    var payload = {
        cat: catCfgTarget,
        apiConfigId: document.getElementById('catCfgApi').value,
        persona: document.getElementById('catCfgPersona').value,
        toolNames: toolNames.join(','),
        injectList: catCfgInjectList,
        qqbotId: document.getElementById('catCfgQqBot').value,
        qqbotEnable: document.getElementById('catCfgQqEnable').checked,
        enabledRoots: collectRootChecks()
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
    if (!isValidInjectPath(val)) {
        document.getElementById('catCfgMsg').textContent = '路径拒绝——只接受完整路径（绝对路径或 id: 命名空间）: ' + val;
        return;
    }
    catCfgInjectList.push(val);
    input.value = '';
    renderInjectList(catCfgInjectList);
};
// 默认猫配置入口——由多猫页 Majordomo 行「配置」按钮承担（F2.1 index 对话页签移除；openCatCfg('majordomo') 见 renderCatRow special 分支）
var elChatCfg = document.getElementById('chatCfg');
if (elChatCfg) { elChatCfg.onclick = function () { openCatCfg('majordomo', 'majordomo'); }; }

// M4e 猫级白名单——启用根勾选（allRoots 全局池；enabledRoots 当前猫已启用；workspace 强制常驻不可取消）
function renderRootChecks(allRoots, enabledRoots) {
    var box = document.getElementById('catCfgRoots');
    box.textContent = '';
    if (!allRoots || allRoots.length === 0) {
        box.textContent = '（全局根池为空）';
        return;
    }
    // 空 enabledRoots = 全量（旧配置无字段）
    var enabled = {};
    if (enabledRoots && enabledRoots.length > 0) {
        for (var i = 0; i < enabledRoots.length; i++) { enabled[enabledRoots[i]] = true; }
    }
    var hasEnabled = enabledRoots && enabledRoots.length > 0;
    for (var j = 0; j < allRoots.length; j++) {
        (function (root) {
            var isWs = root.id === 'workspace' || root.id === 'runtime';
            var label = document.createElement('label');
            label.style.cssText = 'display:flex;align-items:center;gap:4px;background:#242424;border:1px solid #2a2a2a;border-radius:4px;padding:3px 8px;font-size:11px;color:#c8c8c8;cursor:pointer';
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.className = 'root-cb';
            cb.setAttribute('data-root', root.id);
            // workspace 强制必选且不可取消；其余默认勾选 = 全量语义
            cb.checked = isWs || !hasEnabled || enabled[root.id] === true;
            if (isWs) {
                cb.checked = true;
                cb.disabled = true;
                label.style.color = '#dcdcaa';
                label.title = '系统根——必选不可取消';
            }
            label.appendChild(cb);
            var txt = document.createElement('span');
            txt.textContent = (isWs ? 'workspace（系统根·必选）' : root.id) + ' — ' + root.path + (root.writable ? '' : ' [只读]');
            txt.style.wordBreak = 'break-all';
            label.appendChild(txt);
            box.appendChild(label);
        })(allRoots[j]);
    }
}

function collectRootChecks() {
    var ids = [];
    var boxes = document.querySelectorAll('#catCfgRoots input[type=checkbox].root-cb');
    for (var i = 0; i < boxes.length; i++) {
        if (boxes[i].checked) { ids.push(boxes[i].getAttribute('data-root')); }
    }
    return ids;
}

// 新猫默认模板编辑面（M3d 体验轮——cat-default.cfg 全局配置：baseRole + 新猫三字段默认值）
var tplInjectList = [];

// 注入路径前端校验——只接受完整路径：盘符/UNC//斜杠开头 或 id: 命名空间（与后端 IsValidInjectPath 同规）
function isValidInjectPath(p) {
    if (!p || p.length === 0) { return false; }
    if (/^[a-zA-Z]:[\\/]/.test(p) || /^[\\/]{2}/.test(p) || /^\//.test(p)) { return true; }
    var m = /^([A-Za-z0-9_]+):(.+)$/.exec(p);
    return m !== null && m[2].length > 0;
}

function renderTplInject() {
    var box = document.getElementById('tplInject');
    box.textContent = '';
    for (var i = 0; i < tplInjectList.length; i++) {
        (function (idx) {
            var row = document.createElement('div');
            row.style.cssText = 'display:flex;gap:6px;align-items:center;margin-top:3px';
            var txt = document.createElement('span');
            txt.textContent = tplInjectList[idx];
            txt.style.cssText = 'flex:1;color:#6a9955;font-size:11px;word-break:break-all';
            row.appendChild(txt);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px';
            rm.onclick = function () {
                tplInjectList.splice(idx, 1);
                renderTplInject();
            };
            row.appendChild(rm);
            box.appendChild(row);
        })(i);
    }
}

function loadTpl() {
    fetch('/api/v1/cat-default')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok) {
                document.getElementById('tplMsg').textContent = '读取失败: ' + (d.error || '');
                return;
            }
            document.getElementById('tplBaseRole').value = d.baseRole || '';
            document.getElementById('tplPersona').value = d.defaultPersona || '';
            renderGroupedChecks('tplTools', d.allTools || d.allToolNames || [], parseToolChecked(d.defaultToolNames || ''));
            tplInjectList = (d.defaultInjectList || []).slice();
            renderTplInject();
        })
        .catch(function () {
            document.getElementById('tplMsg').textContent = '读取失败——宿主未运行？';
        });
}

function saveTpl() {
    var payload = {
        baseRole: document.getElementById('tplBaseRole').value,
        defaultPersona: document.getElementById('tplPersona').value,
        defaultToolNames: collectChecked('tplTools').join(','),
        defaultInjectList: tplInjectList
    };
    fetch('/api/v1/cat-default', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            document.getElementById('tplMsg').textContent = d.ok ? '已保存——新猫创建时继承；baseRole 新会话生效' : '保存失败: ' + d.error;
        });
}

document.getElementById('tplRefresh').onclick = loadTpl;
document.getElementById('tplSave').onclick = saveTpl;
document.getElementById('tplInjectAddBtn').onclick = function () {
    var input = document.getElementById('tplInjectAdd');
    var val = input.value.trim();
    if (val.length === 0) { return; }
    if (!isValidInjectPath(val)) {
        document.getElementById('tplMsg').textContent = '路径拒绝——只接受完整路径（绝对路径或 id: 命名空间）: ' + val;
        return;
    }
    tplInjectList.push(val);
    input.value = '';
    renderTplInject();
};
loadTpl();

// [段] 受控根编辑面（M3d 体验轮——workspace roots 管理员面；重启生效；LLM 工具面只读）
var rootsList = [];

function renderRoots() {
    var body = document.querySelector('#rootsTable tbody');
    body.textContent = '';
    for (var i = 0; i < rootsList.length; i++) {
        (function (idx) {
            var tr = document.createElement('tr');
            var tdId = document.createElement('td');
            tdId.textContent = rootsList[idx].id;
            tdId.style.color = '#c586c0';
            tr.appendChild(tdId);
            var tdPath = document.createElement('td');
            tdPath.textContent = rootsList[idx].path;
            tdPath.style.wordBreak = 'break-all';
            tr.appendChild(tdPath);
            var tdW = document.createElement('td');
            tdW.textContent = rootsList[idx].writable ? '读写' : '只读';
            tdW.style.color = rootsList[idx].writable ? '#4ec9b0' : '#f48771';
            tr.appendChild(tdW);
            var tdOp = document.createElement('td');
            var editBtn = document.createElement('button');
            editBtn.textContent = '编辑';
            editBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#4ec9b0;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
            editBtn.onclick = function () {
                enterEditRoot(idx);
            };
            tdOp.appendChild(editBtn);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#f48771;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px';
            rm.onclick = function () {
                rootsList.splice(idx, 1);
                renderRoots();
            };
            tdOp.appendChild(rm);
            tr.appendChild(tdOp);
            body.appendChild(tr);
        })(i);
    }
}

// 行内编辑——把指定行切换为输入框（id/path/writable 可改；确定/取消）
function enterEditRoot(idx) {
    var body = document.querySelector('#rootsTable tbody');
    body.textContent = '';
    for (var i = 0; i < rootsList.length; i++) {
        (function (rowIdx) {
            var tr = document.createElement('tr');
            if (rowIdx === idx) {
                var tdId = document.createElement('td');
                var idIn = document.createElement('input');
                idIn.value = rootsList[rowIdx].id;
                idIn.style.cssText = 'width:90%;background:#1a1a1a;border:1px solid #2a2a2a;color:#c8c8c8;padding:3px 6px;font-family:inherit;font-size:11px';
                tdId.appendChild(idIn);
                tr.appendChild(tdId);
                var tdPath = document.createElement('td');
                var pathIn = document.createElement('input');
                pathIn.value = rootsList[rowIdx].path;
                pathIn.style.cssText = 'width:96%;background:#1a1a1a;border:1px solid #2a2a2a;color:#c8c8c8;padding:3px 6px;font-family:inherit;font-size:11px';
                tdPath.appendChild(pathIn);
                tr.appendChild(tdPath);
                var tdW = document.createElement('td');
                var wSel = document.createElement('select');
                var optW = document.createElement('option');
                optW.value = 'true';
                optW.textContent = '读写';
                var optR = document.createElement('option');
                optR.value = 'false';
                optR.textContent = '只读';
                wSel.appendChild(optW);
                wSel.appendChild(optR);
                wSel.value = rootsList[rowIdx].writable ? 'true' : 'false';
                wSel.style.cssText = 'background:#1a1a1a;border:1px solid #2a2a2a;color:#c8c8c8;padding:3px 6px;font-family:inherit;font-size:11px';
                tdW.appendChild(wSel);
                tr.appendChild(tdW);
                var tdOp = document.createElement('td');
                var okBtn = document.createElement('button');
                okBtn.textContent = '确定';
                okBtn.style.cssText = 'background:#0e639c;border:none;color:#fff;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px;margin-right:4px';
                okBtn.onclick = function () {
                    var nid = idIn.value.trim();
                    var npath = pathIn.value.trim();
                    var nw = wSel.value === 'true';
                    if (nid.length === 0 || npath.length === 0) {
                        document.getElementById('rootsMsg').textContent = 'id 和路径不能为空';
                        return;
                    }
                    if (!/^[A-Za-z0-9_]+$/.test(nid)) {
                        document.getElementById('rootsMsg').textContent = 'id 非法——仅字母/数字/下划线';
                        return;
                    }
                    for (var k = 0; k < rootsList.length; k++) {
                        if (k !== rowIdx && rootsList[k].id === nid) {
                            document.getElementById('rootsMsg').textContent = 'id 重复: ' + nid;
                            return;
                        }
                    }
                    rootsList[rowIdx] = { id: nid, path: npath, writable: nw };
                    renderRoots();
                };
                tdOp.appendChild(okBtn);
                var cancelBtn = document.createElement('button');
                cancelBtn.textContent = '取消';
                cancelBtn.style.cssText = 'background:#1f1f1f;border:1px solid #2a2a2a;color:#9a9a9a;padding:2px 8px;cursor:pointer;font-family:inherit;font-size:11px';
                cancelBtn.onclick = function () {
                    renderRoots();
                };
                tdOp.appendChild(cancelBtn);
                tr.appendChild(tdOp);
            } else {
                var tdId2 = document.createElement('td');
                tdId2.textContent = rootsList[rowIdx].id;
                tdId2.style.color = '#c586c0';
                tr.appendChild(tdId2);
                var tdPath2 = document.createElement('td');
                tdPath2.textContent = rootsList[rowIdx].path;
                tdPath2.style.wordBreak = 'break-all';
                tr.appendChild(tdPath2);
                var tdW2 = document.createElement('td');
                tdW2.textContent = rootsList[rowIdx].writable ? '读写' : '只读';
                tdW2.style.color = rootsList[rowIdx].writable ? '#4ec9b0' : '#f48771';
                tr.appendChild(tdW2);
                var tdOp2 = document.createElement('td');
                tdOp2.textContent = '';
                tr.appendChild(tdOp2);
            }
            body.appendChild(tr);
        })(i);
    }
}

function loadRoots() {
    fetch('/api/v1/workspace')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok) {
                document.getElementById('rootsMsg').textContent = '读取失败: ' + (d.error || '');
                return;
            }
            rootsList = (d.roots || []).slice();
            renderRoots();
        })
        .catch(function () {
            document.getElementById('rootsMsg').textContent = '读取失败——宿主未运行？';
        });
}

function saveRoots() {
    var payload = { roots: rootsList };
    fetch('/api/v1/workspace', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            document.getElementById('rootsMsg').textContent = d.ok ? '已保存——重启宿主生效' : '保存失败: ' + (d.error || '');
        });
}

document.getElementById('rootAddBtn').onclick = function () {
    var id = document.getElementById('rootNewId').value.trim();
    var path = document.getElementById('rootNewPath').value.trim();
    var writable = document.getElementById('rootNewWritable').value === 'true';
    if (id.length === 0 || path.length === 0) {
        document.getElementById('rootsMsg').textContent = 'id 和路径不能为空';
        return;
    }
    if (!/^[A-Za-z0-9_]+$/.test(id)) {
        document.getElementById('rootsMsg').textContent = 'id 非法——仅字母/数字/下划线';
        return;
    }
    for (var i = 0; i < rootsList.length; i++) {
        if (rootsList[i].id === id) {
            document.getElementById('rootsMsg').textContent = 'id 重复: ' + id;
            return;
        }
    }
    rootsList.push({ id: id, path: path, writable: writable });
    document.getElementById('rootNewId').value = '';
    document.getElementById('rootNewPath').value = '';
    renderRoots();
};
document.getElementById('rootsSave').onclick = saveRoots;
loadRoots();
