// CH4 外观层——panel.js：多猫管理区（2026-08-25 拆分自 index.html；2026-09-08 体量治理：API/QQBot 池 → panel-apis.js；每猫配置/受控根 → panel-catcfg.js）
// 依赖：app.js 先加载（全局状态与 SSE 已就位）
// 加载顺序：app.js → panel.js → panel-apis.js → panel-catcfg.js（index.html 引用）

// [段13] 多猫管理区（P9.3d）——GET /api/v1/cats 列表 + POST command cat.* 指令族
// 2026-10-01 主面板轮：ID 列撤除；新增 LLM API / QQ Bot（行内直改）+ 目录白名单 / 工具清单（专用弹层入口）
var catsTableBody = document.querySelector('#catsTable tbody');
var catsMsgEl = document.getElementById('catsMsg');
var catNewInput = document.getElementById('catNewName');
// 行内编辑数据面——API 池 / QQ Bot 池 / 全局根池（首屏加载一次，渲染时查表）
var catApiOptions = [];
var catQqBotOptions = [];
var catAllRoots = [];

// 池数据加载——三源就绪后渲染列表（任一失败不阻塞：降级为空池）
function loadCatPools() {
    var pending = 3;
    function step() {
        pending = pending - 1;
        if (pending === 0) { loadCats(); }
    }
    fetch('/api/v1/llm-apis')
        .then(function (r) { return r.json(); })
        .then(function (d) { catApiOptions = d.items || []; step(); })
        .catch(function () { step(); });
    fetch('/api/v1/qqbot-apis')
        .then(function (r) { return r.json(); })
        .then(function (d) { catQqBotOptions = d.items || []; step(); })
        .catch(function () { step(); });
    fetch('/api/v1/workspace')
        .then(function (r) { return r.json(); })
        .then(function (d) { catAllRoots = d.roots || []; step(); })
        .catch(function () { step(); });
}

// 猫列表加载——渲染表格（名称/LLM API/QQ Bot/目录白名单/工具清单/状态/端口/操作）
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

// 单猫行渲染——名称/LLM API/QQ Bot 行内直改；目录白名单 + 工具清单走专用弹层；操作按钮按状态启用（special 强制自启 + 无停止/删除）
function renderCatRow(cat) {
    var tr = document.createElement('tr');
    // 行点击入口——整行可点开会话详情（排除按钮 / 输入类控件；事件委托见文件末 tbody 绑定）
    tr.setAttribute('data-cat', cat.id);
    tr.setAttribute('data-name', cat.name || cat.id);
    // [列首] 会话状态——phase 徽标 + Note 标（上行）/ 前文 tokens · 轮次 · 消息 · 待处理 · 前文变动（下行）
    // 数据面 = /api/v1/cats 会话字段（BuildCatInfoEntry 产物——与 cat.info / 快照 sessions 段同源）
    var tdState = document.createElement('td');
    var stateWrap = document.createElement('div');
    stateWrap.className = 'cat-state';
    var stateHead = document.createElement('div');
    stateHead.className = 'cat-state-head';
    if (cat.running) {
        if (cat.phase) {
            var phaseBadge = document.createElement('span');
            phaseBadge.className = 'session-badge ph-' + cat.phase;
            phaseBadge.textContent = cat.phase;
            stateHead.appendChild(phaseBadge);
        } else {
            var runTag = document.createElement('span');
            runTag.className = 'cat-state-run';
            runTag.textContent = '运行中';
            stateHead.appendChild(runTag);
        }
    } else {
        var idleTag = document.createElement('span');
        idleTag.className = 'cat-state-idle';
        idleTag.textContent = '静默';
        stateHead.appendChild(idleTag);
    }
    if (cat.noteActive) {
        var stateNote = document.createElement('span');
        stateNote.className = 'session-note';
        stateNote.textContent = '📋 Note';
        stateHead.appendChild(stateNote);
    }
    stateWrap.appendChild(stateHead);
    var stateBits = [];
    if (typeof cat.context !== 'undefined' && cat.context !== null) {
        stateBits.push('前文 ' + cat.context + ' tokens' + (cat.contextCount ? ' / ' + cat.contextCount + ' 条' : ''));
    }
    if (typeof cat.round !== 'undefined' && cat.round !== null) {
        stateBits.push('轮次 ' + cat.round + ' · 消息 ' + cat.msgCount + ' · 待处理 ' + cat.pending);
    }
    var stateAgo = fmtAgo(cat.lastContextChangeAt);
    if (stateAgo) {
        stateBits.push('前文变动 ' + stateAgo);
    }
    if (stateBits.length > 0) {
        var stateInfo = document.createElement('div');
        stateInfo.className = 'cat-state-info';
        stateInfo.textContent = stateBits.join(' · ');
        stateWrap.appendChild(stateInfo);
    }
    tdState.appendChild(stateWrap);
    tr.appendChild(tdState);
    // [列1] 名称——行内编辑（未改动 / 清空 = 不提交；special 行 ★ 前缀独立于输入框）
    var tdName = document.createElement('td');
    var nameWrap = document.createElement('div');
    nameWrap.style.cssText = 'display:flex;align-items:center;gap:4px';
    if (cat.special) {
        var star = document.createElement('span');
        star.textContent = '★';
        star.style.color = 'var(--ch-warn)';
        nameWrap.appendChild(star);
    }
    var nameInput = document.createElement('input');
    nameInput.className = 'input-mini';
    nameInput.style.flex = '1';
    nameInput.style.minWidth = '0';
    nameInput.style.color = 'var(--ch-identity)';
    nameInput.value = cat.name || '';
    nameInput.placeholder = '名称';
    wireInlineEdit(nameInput, cat.name || '', function (v) { submitCatField(cat, 'displayName', v, '名称'); });
    nameWrap.appendChild(nameInput);
    tdName.appendChild(nameWrap);
    tr.appendChild(tdName);

    // [列2] LLM API——下拉直改（首项「默认 · 跟随全局」= 零值语义）
    var tdApi = document.createElement('td');
    var apiSel = document.createElement('select');
    apiSel.className = 'input-mini';
    apiSel.style.width = '100%';
    var apiDefOpt = document.createElement('option');
    apiDefOpt.value = '';
    apiDefOpt.textContent = '（默认 · 跟随全局）';
    apiSel.appendChild(apiDefOpt);
    for (var a = 0; a < catApiOptions.length; a++) {
        var apiOpt = document.createElement('option');
        apiOpt.value = catApiOptions[a].apiConfigId;
        var apiLabel = catApiOptions[a].displayName || catApiOptions[a].apiConfigId;
        if (catApiOptions[a].isDefault) { apiLabel = apiLabel + ' · 全局默认'; }
        apiOpt.textContent = apiLabel;
        apiSel.appendChild(apiOpt);
    }
    if (isDefaultApiValue(cat.apiConfigId)) { apiSel.value = ''; } else { apiSel.value = cat.apiConfigId; }
    apiSel.addEventListener('change', function () { submitCatField(cat, 'apiConfigId', apiSel.value, 'LLM API'); });
    tdApi.appendChild(apiSel);
    tr.appendChild(tdApi);

    // [列3] QQ Bot——下拉直改（空 = 未绑定；已被他猫占用项禁用）
    var tdQq = document.createElement('td');
    var qqSel = document.createElement('select');
    qqSel.className = 'input-mini';
    qqSel.style.width = '100%';
    var qqNone = document.createElement('option');
    qqNone.value = '';
    qqNone.textContent = '（未绑定）';
    qqSel.appendChild(qqNone);
    for (var q = 0; q < catQqBotOptions.length; q++) {
        var qqOpt = document.createElement('option');
        qqOpt.value = catQqBotOptions[q].qqBotId;
        var qqLabel = catQqBotOptions[q].displayName || catQqBotOptions[q].qqBotId;
        if (catQqBotOptions[q].boundCat && catQqBotOptions[q].qqBotId !== cat.qqbotId) {
            qqLabel = qqLabel + '（已被 ' + catQqBotOptions[q].boundCat + ' 绑定）';
            qqOpt.disabled = true;
        }
        qqOpt.textContent = qqLabel;
        qqSel.appendChild(qqOpt);
    }
    qqSel.value = cat.qqbotId || '';
    qqSel.addEventListener('change', function () { submitCatField(cat, 'qqbotId', qqSel.value, 'QQ Bot'); });
    tdQq.appendChild(qqSel);
    tr.appendChild(tdQq);

    // [列4] 目录白名单——显示无权目录 + 设置入口（专用弹层）
    var tdRoots = document.createElement('td');
    var denied = collectDeniedRoots(cat);
    var deniedSpan = document.createElement('span');
    deniedSpan.style.fontSize = 'var(--ch-fs-tag)';
    if (denied.length === 0) {
        deniedSpan.textContent = '（全部可访问）';
        deniedSpan.style.color = 'var(--ch-fg-weak)';
    } else {
        deniedSpan.textContent = '无权: ' + denied.join(', ');
        deniedSpan.style.color = 'var(--ch-err)';
        deniedSpan.title = '本猫不可访问的根——点「设置」调整';
    }
    tdRoots.appendChild(deniedSpan);
    var rootsBtn = document.createElement('button');
    rootsBtn.textContent = '设置';
    rootsBtn.className = 'btn-mini';
    rootsBtn.style.marginLeft = '4px';
    rootsBtn.onclick = function () { openCatRoots(cat.id, cat.name); };
    tdRoots.appendChild(rootsBtn);
    tr.appendChild(tdRoots);

    // [列5] 工具清单——单按钮入口（专用弹层：工具勾选 + 人设）
    var tdTools = document.createElement('td');
    var toolsBtn = document.createElement('button');
    toolsBtn.textContent = '工具清单';
    toolsBtn.className = 'btn-mini';
    toolsBtn.onclick = function () { openCatTools(cat.id, cat.name); };
    tdTools.appendChild(toolsBtn);
    tr.appendChild(tdTools);
    var tdPort = document.createElement('td');
    tdPort.textContent = cat.running ? (':' + cat.port) : '-';
    tr.appendChild(tdPort);
    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    if (cat.running) {
        var openBtn = document.createElement('button');
        openBtn.textContent = '打开对话';
        openBtn.className = 'btn-mini primary';
        openBtn.onclick = function () { window.open('http://127.0.0.1:' + cat.port + '/', '_blank'); };
        tdOp.appendChild(openBtn);
        if (!cat.special) {
            var stopBtn = document.createElement('button');
            stopBtn.textContent = '停止';
            stopBtn.className = 'btn-mini';
            stopBtn.onclick = function () { catAction('cat.stop ' + cat.id); };
            tdOp.appendChild(stopBtn);
        }
    } else {
        var startBtn = document.createElement('button');
        startBtn.textContent = '启动';
        startBtn.className = 'btn-mini primary';
        startBtn.onclick = function () { catAction('cat.start ' + cat.id); };
        tdOp.appendChild(startBtn);
    }
    // 配置入口——special（Majordomo）与多猫共用（openCatCfg 由 panel-catcfg.js 提供）
    var cfgBtn = document.createElement('button');
    cfgBtn.textContent = '配置';
    cfgBtn.className = 'btn-mini';
    cfgBtn.onclick = function () { openCatCfg(cat.id, cat.name); };
    tdOp.appendChild(cfgBtn);
    if (!cat.special) {
        // 多猫专属——删除（Majordomo 特殊会话无删除入口）
        var delBtn = document.createElement('button');
        delBtn.textContent = '删除';
        delBtn.className = 'btn-mini danger';
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

// 新建猫——cat.new [显示名]（空名 = 后端自动命名：默认小猫 / 默认小猫(1)…）
function catNew() {
    var name = catNewInput.value.trim();
    catNewInput.value = '';
    if (name.length === 0) {
        catAction('cat.new');
        return;
    }
    catAction('cat.new ' + name);
}

// 整行点击——打开会话详情弹层（openCatDetail 在 app.js；只点「内容区」才开）
// 判据：点击目标或其祖先链上出现 button / input / select / textarea / a / label → 视为操作，不触发详情
function isRowInteractive(el) {
    while (el && el.tagName) {
        var tag = el.tagName.toLowerCase();
        if (tag === 'button' || tag === 'input' || tag === 'select' || tag === 'textarea' || tag === 'a' || tag === 'label') {
            return true;
        }
        el = el.parentNode;
    }
    return false;
}

function onCatsRowClick(e) {
    if (isRowInteractive(e.target)) { return; }
    var el = e.target;
    while (el && el !== catsTableBody) {
        if (el.tagName && el.tagName.toLowerCase() === 'tr') {
            var key = el.getAttribute('data-cat');
            if (key) { openCatDetail(key, el.getAttribute('data-name') || key); }
            return;
        }
        el = el.parentNode;
    }
}

catsTableBody.addEventListener('click', onCatsRowClick);

// 多猫区按钮绑定 + 列表初始化
document.getElementById('catNewBtn').addEventListener('click', catNew);
document.getElementById('catsRefresh').addEventListener('click', loadCats);
catNewInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { catNew(); }
});
loadCatPools();

// 无权目录——全局根池 ∖ 本猫启用根（workspace/runtime 系统根恒可用，不计入无权；未配置 = 空白名单）
function collectDeniedRoots(cat) {
    var denied = [];
    if (!catAllRoots || catAllRoots.length === 0) { return denied; }
    var enabled = {};
    var list = cat.enabledRoots || [];
    for (var i = 0; i < list.length; i++) {
        if (list[i]) { enabled[String(list[i]).toLowerCase()] = true; }
    }
    for (var j = 0; j < catAllRoots.length; j++) {
        var root = catAllRoots[j];
        var rid = (root.id || '').toLowerCase();
        if (rid === 'workspace' || rid === 'runtime') { continue; }
        if (enabled[rid] === true) { continue; }
        denied.push(root.id);
    }
    return denied;
}

// 主面板行内字段提交——POST /api/v1/cat-config（字段级合并写：出现即覆盖 / 缺省即保留）
// 2026-10-01 修正：原走 cat.cfg.set 文本指令（HTTP 线程入队主线程泵，回执只报"已受理"）——空值被行首 Trim 吃掉（改成默认 API / 解绑 QQ Bot 必然失败），
// 且写入未落盘就先重建列表（固定 400ms）→ 表现为「改完瞬间跳回原值」+「显示与实际不符」。
// 现改走端点：同步落盘、回执即结果、落盘后才刷新。空值语义 = 清空（apiConfigId 回默认 / qqbotId 解绑 / displayName 拒绝空值）。
function submitCatField(cat, field, value, label) {
    catsMsgEl.textContent = '保存中…（' + label + '）';
    var payload = { cat: cat.id };
    payload[field] = value;
    fetch('/api/v1/cat-config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (d.ok) {
                catsMsgEl.textContent = '已保存（' + label + '）';
                loadCats();
            } else {
                catsMsgEl.textContent = '保存失败（' + label + '）: ' + (d.error || '');
            }
        })
        .catch(function (e) {
            catsMsgEl.textContent = '请求失败: ' + e.message;
        });
}
