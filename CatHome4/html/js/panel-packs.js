// CH4 外观层——panel-packs.js：加载包池管理面（R4 加载包）——池 CRUD（GET/POST /api/v1/packs）
// 落位：配置页「加载包池」区（内联表格 + 新建/编辑表单；index.html #packsTable / #packKey …）
// 每猫挂载勾选在 panel-catcfg.js（本文件只管池本身）

var packsPool = [];

function loadPacks() {
    fetch('/api/v1/packs')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d.ok) {
                document.getElementById('packsMsg').textContent = '读取失败: ' + (d.error || '');
                return;
            }
            packsPool = d.packs || [];
            renderPacksTable();
        })
        .catch(function () {
            document.getElementById('packsMsg').textContent = '读取失败——宿主未运行？';
        });
}

function renderPacksTable() {
    var tbody = document.getElementById('packsTbody');
    tbody.textContent = '';
    if (packsPool.length === 0) {
        var tr = document.createElement('tr');
        var td = document.createElement('td');
        td.colSpan = 4;
        td.textContent = '（池为空——在下方新建）';
        td.style.cssText = 'color:var(--ch-fg-weak);font-size:var(--ch-fs-tag)';
        tr.appendChild(td);
        tbody.appendChild(tr);
        return;
    }
    for (var i = 0; i < packsPool.length; i++) {
        (function (pack) {
            var tr = document.createElement('tr');
            var k = document.createElement('td');
            k.textContent = pack.key;
            k.style.cssText = 'color:var(--ch-warn);font-size:var(--ch-fs-tag)';
            tr.appendChild(k);
            var dm = document.createElement('td');
            dm.textContent = pack.desc || '';
            dm.style.cssText = 'color:var(--ch-fg);font-size:var(--ch-fs-tag)';
            tr.appendChild(dm);
            var pc = document.createElement('td');
            var paths = pack.paths || [];
            pc.textContent = paths.length;
            pc.title = paths.join('\n');
            pc.style.cssText = 'color:var(--ch-fg-muted);font-size:var(--ch-fs-tag)';
            tr.appendChild(pc);
            var op = document.createElement('td');
            var eb = document.createElement('button');
            eb.textContent = '编辑';
            eb.className = 'btn-mini';
            eb.onclick = function () { editPack(pack.key); };
            op.appendChild(eb);
            var db = document.createElement('button');
            db.textContent = '删除';
            db.className = 'btn-mini tight danger';
            db.onclick = function () { deletePack(pack.key); };
            op.appendChild(db);
            tr.appendChild(op);
            tbody.appendChild(tr);
        })(packsPool[i]);
    }
}

function editPack(key) {
    for (var i = 0; i < packsPool.length; i++) {
        if (packsPool[i].key === key) {
            document.getElementById('packKey').value = packsPool[i].key;
            document.getElementById('packDesc').value = packsPool[i].desc || '';
            document.getElementById('packPaths').value = (packsPool[i].paths || []).join('\n');
            document.getElementById('packsMsg').textContent = '已载入「' + key + '」——修改后点保存（同名覆盖）';
            return;
        }
    }
}

function clearPackForm() {
    document.getElementById('packKey').value = '';
    document.getElementById('packDesc').value = '';
    document.getElementById('packPaths').value = '';
    document.getElementById('packsMsg').textContent = '新包——填 key / 描述 / 路径（一行一个）后保存';
}

// 路径文本 → 清单——去空白行（一行一个；目录项加载其一级 *.md）
function parsePackPaths(text) {
    var out = [];
    if (!text) { return out; }
    var lines = text.split('\n');
    for (var i = 0; i < lines.length; i++) {
        var v = lines[i].trim();
        if (v.length > 0) { out.push(v); }
    }
    return out;
}

function savePack() {
    var key = document.getElementById('packKey').value.trim();
    var desc = document.getElementById('packDesc').value.trim();
    var paths = parsePackPaths(document.getElementById('packPaths').value);
    if (key.length === 0) {
        document.getElementById('packsMsg').textContent = 'key 不能为空';
        return;
    }
    if (paths.length === 0) {
        document.getElementById('packsMsg').textContent = 'paths 不能为空——一行一个路径';
        return;
    }
    fetch('/api/v1/packs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key: key, desc: desc, paths: paths })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            document.getElementById('packsMsg').textContent = d.ok ? '已保存：' + key + '（' + paths.length + ' 个路径项）' : '保存失败: ' + d.error;
            if (d.ok) { loadPacks(); }
        });
}

function deletePack(key) {
    if (!window.confirm('删除包「' + key + '」？已挂载该包的猫将无法调用（重建同名包即恢复）')) { return; }
    fetch('/api/v1/packs/delete', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ key: key })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            document.getElementById('packsMsg').textContent = d.ok ? '已删除：' + key : '删除失败: ' + d.error;
            if (d.ok) { loadPacks(); }
        });
}

document.getElementById('packsRefresh').onclick = loadPacks;
document.getElementById('packNew').onclick = clearPackForm;
document.getElementById('packSave').onclick = savePack;
// 配置页常驻——页面加载即拉取池现状
loadPacks();
