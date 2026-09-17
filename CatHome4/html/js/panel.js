// CH4 外观层——panel.js：多猫管理区（2026-08-25 拆分自 index.html；2026-09-08 体量治理：API/QQBot 池 → panel-apis.js；每猫配置/受控根 → panel-catcfg.js）
// 依赖：app.js 先加载（全局状态与 SSE 已就位）
// 加载顺序：app.js → panel.js → panel-apis.js → panel-catcfg.js（index.html 引用）

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
    tdName.style.color = 'var(--ch-identity)';
    tr.appendChild(tdName);
    var tdId = document.createElement('td');
    tdId.textContent = cat.id || '';
    tdId.style.color = 'var(--ch-fg-weak)';
    tr.appendChild(tdId);
    var tdState = document.createElement('td');
    if (cat.running) {
        tdState.textContent = '运行中';
        tdState.style.color = 'var(--ch-ok)';
    } else {
        tdState.textContent = '静默';
        tdState.style.color = 'var(--ch-fg-weak)';
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
document.getElementById('catsRefresh').addEventListener('click', loadCats);
catNewInput.addEventListener('keydown', function (e) {
    if (e.key === 'Enter') { catNew(); }
});
loadCats();
