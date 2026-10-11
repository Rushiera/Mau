// CH4 外观层——panel.js：多猫管理区（2026-08-25 拆分自 index.html；2026-09-08 体量治理：API/QQBot 池 → panel-apis.js；每猫配置/受控根 → panel-catcfg.js）
// 依赖：app.js 先加载（全局状态与 SSE 已就位）
// 加载顺序：app.js → panel.js → panel-apis.js → panel-catcfg.js（index.html 引用）

// [段13] 多猫管理区（P9.3d；A223 List→Card 自然排布 + 分组容器）——GET /api/v1/cats 列表 + POST command cat.* 指令族
var catsGroupsEl = document.getElementById('catsGroups');
var catsMsgEl = document.getElementById('catsMsg');
var catNewInput = document.getElementById('catNewName');

// 猫列表加载——渲染卡片（分组容器，A223）
function loadCats() {
    fetch('/api/v1/cats')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            catsGroupsEl.textContent = '';
            var cats = d.cats || [];
            if (cats.length === 0) {
                catsMsgEl.textContent = '暂无猫——输入显示名新建';
                return;
            }
            catsMsgEl.textContent = cats.length + ' 只猫';
            // F2.1 前端唯一化——special 猫（Majordomo）置顶（固化入口）；分组键 = 会话 type 标签（A224 接线）
            renderCatGroups(groupCats(cats));
        })
        .catch(function () {
            catsMsgEl.textContent = '列表加载失败——宿主未运行？';
        });
}

// 分组键——special（Majordomo）固定 'majordomo' 组（独立一排）；其余按会话 type 标签（A224 接线），缺字段 / 空串 → 默认组（空键）
function catGroupKey(cat) {
    if (cat && cat.special) {
        return 'majordomo';
    }
    if (cat && cat.type) {
        return String(cat.type).trim();
    }
    return '';
}

// 分组标签——空键即默认组
function catGroupLabel(key) {
    return key.length > 0 ? key : '默认';
}

// 分组——special 猫（Majordomo）置顶（F2.1 固化入口），再按 type 分区（插入序 = 组序）
function groupCats(cats) {
    var ordered = [];
    var i;
    for (i = 0; i < cats.length; i = i + 1) {
        if (cats[i].special) { ordered.push(cats[i]); }
    }
    for (i = 0; i < cats.length; i = i + 1) {
        if (!cats[i].special) { ordered.push(cats[i]); }
    }
    var groups = [];
    var index = {};
    for (i = 0; i < ordered.length; i = i + 1) {
        var key = catGroupKey(ordered[i]);
        if (index[key] === undefined) {
            index[key] = groups.length;
            groups.push({ key: key, cats: [] });
        }
        groups[index[key]].cats.push(ordered[i]);
    }
    return groups;
}

// 分组渲染——单组时不显组标题（A223 现全落默认组；A224 多组时标题自动出现）
function renderCatGroups(groups) {
    var showTitle = groups.length > 1;
    for (var g = 0; g < groups.length; g = g + 1) {
        var group = document.createElement('div');
        group.className = 'cat-group';
        if (showTitle) {
            var title = document.createElement('div');
            title.className = 'cat-group-title';
            title.textContent = catGroupLabel(groups[g].key);
            group.appendChild(title);
        }
        var grid = document.createElement('div');
        grid.className = 'cat-grid cat-grid-manage';
        for (var c = 0; c < groups[g].cats.length; c = c + 1) {
            grid.appendChild(renderCatCard(groups[g].cats[c]));
        }
        group.appendChild(grid);
        catsGroupsEl.appendChild(group);
    }
}

// 单猫卡片渲染（A223）——操作按钮按状态启用（启动/停止互斥）；special（Majordomo）强制自启 + 无停止/删除
function renderCatCard(cat) {
    var card = document.createElement('div');
    card.className = 'cat-card';
    var head = document.createElement('div');
    head.className = 'cat-head';
    var name = document.createElement('span');
    name.className = 'cat-name';
    name.textContent = (cat.special ? '★ ' : '') + (cat.name || '');
    head.appendChild(name);
    var badge = document.createElement('span');
    if (cat.running) {
        badge.className = 'cat-badge run';
        badge.textContent = '● 已启动';
    } else {
        badge.className = 'cat-badge idle';
        badge.textContent = '○ 未启动';
    }
    head.appendChild(badge);
    card.appendChild(head);
    // 信息行——ID + 端口（头行只留名称与状态徽标；长 ID 同排不再挤头行）
    var meta = document.createElement('div');
    meta.className = 'cat-meta';
    var id = document.createElement('span');
    id.className = 'cat-id';
    id.textContent = '#' + (cat.id || '');
    meta.appendChild(id);
    var port = document.createElement('span');
    port.className = 'cat-port';
    port.textContent = '端口 ' + (cat.running ? (':' + cat.port) : '-');
    meta.appendChild(port);
    card.appendChild(meta);
    // 操作行
    var ops = document.createElement('div');
    ops.className = 'cat-ops';
    if (cat.running) {
        var openBtn = document.createElement('button');
        openBtn.textContent = '打开对话';
        openBtn.className = 'btn-mini primary';
        openBtn.onclick = function () { window.open('http://127.0.0.1:' + cat.port + '/', '_blank'); };
        ops.appendChild(openBtn);
        if (!cat.special) {
            var stopBtn = document.createElement('button');
            stopBtn.textContent = '停止';
            stopBtn.className = 'btn-mini';
            stopBtn.onclick = function () { catAction('cat.stop ' + cat.id); };
            ops.appendChild(stopBtn);
        }
    } else {
        var startBtn = document.createElement('button');
        startBtn.textContent = '启动';
        startBtn.className = 'btn-mini primary';
        startBtn.onclick = function () { catAction('cat.start ' + cat.id); };
        ops.appendChild(startBtn);
    }
    // 配置入口——special（Majordomo）与多猫共用（openCatCfg 由 panel-catcfg.js 提供）
    var cfgBtn = document.createElement('button');
    cfgBtn.textContent = '配置';
    cfgBtn.className = 'btn-mini';
    cfgBtn.onclick = function () { openCatCfg(cat.id, cat.name); };
    ops.appendChild(cfgBtn);
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
        ops.appendChild(delBtn);
    }
    card.appendChild(ops);
    return card;
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
