// CH4 外观层——panel-catcfg.js：每猫配置弹层 + 新猫默认模板 + 受控根编辑面（M3c/M3d/M4e）——panel.js 拆分（2026-09-08 体量治理）
// 依赖：app.js + panel.js 先加载（全局状态已就位；renderCatRow 点击时调用本文件 openCatCfg）；本文件承载 cat.cfg 配置弹层与 workspace 管理
// 段15（M3c 每猫配置）+ 段[受控根编辑面]（M3d）原样迁出，逻辑零改动

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

// M3c API 下拉——首项「默认（跟随全局）」= 零值语义（后端 apiConfigId 清空 → 走 ResolveDefault 实时解析）
function isDefaultApiValue(value) {
    return !value || value.length === 0 || value === '00000000-0000-0000-0000-000000000000';
}

function renderApiOptions(options, current) {
    var sel = document.getElementById('catCfgApi');
    sel.textContent = '';
    var def = document.createElement('option');
    def.value = '';
    def.textContent = '（默认 · 跟随全局默认端点）';
    if (isDefaultApiValue(current)) { def.selected = true; }
    sel.appendChild(def);
    for (var i = 0; i < options.length; i++) {
        var opt = document.createElement('option');
        opt.value = options[i].apiConfigId;
        var label = options[i].displayName + ' (' + options[i].apiConfigId.substring(0, 8) + '…)';
        if (options[i].isDefault) { label = label + ' · 全局默认'; }
        opt.textContent = label;
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

// 拖拽排序（最简原生实现——容器级事件委托；行设 draggable=true 即可拖；onReorder(from, to) 自行搬移数组并重渲染）
function enableDragSort(boxId, onReorder) {
    var box = document.getElementById(boxId);
    var from = -1;
    function findRow(node) {
        while (node && node.parentNode !== box) { node = node.parentNode; }
        return node ? node : null;
    }
    box.addEventListener('dragstart', function (e) {
        var row = findRow(e.target);
        if (!row) { return; }
        from = Array.prototype.indexOf.call(box.children, row);
        if (e.dataTransfer) { e.dataTransfer.effectAllowed = 'move'; }
    });
    box.addEventListener('dragover', function (e) {
        if (from >= 0) { e.preventDefault(); }
    });
    box.addEventListener('drop', function (e) {
        var row = findRow(e.target);
        if (from < 0 || !row) { return; }
        e.preventDefault();
        var to = Array.prototype.indexOf.call(box.children, row);
        var moveFrom = from;
        from = -1;
        if (to >= 0 && to !== moveFrom) { onReorder(moveFrom, to); }
    });
    box.addEventListener('dragend', function () { from = -1; });
}

function renderInjectList(list) {
    catCfgInjectList = list.slice();
    var box = document.getElementById('catCfgInject');
    box.textContent = '';
    for (var i = 0; i < catCfgInjectList.length; i++) {
        (function (idx) {
            var row = document.createElement('div');
            row.style.cssText = 'display:flex;gap:6px;align-items:center;margin-top:3px';
            row.draggable = true;
            row.title = '拖动调整顺序（保存后生效）';
            var handle = document.createElement('span');
            handle.textContent = '≡';
            handle.style.cssText = 'color:#6a6a6a;font-size:12px;cursor:move';
            row.appendChild(handle);
            var txt = document.createElement('span');
            txt.textContent = catCfgInjectList[idx];
            txt.style.cssText = 'flex:1;color:#6a9955;font-size:11px;word-break:break-all';
            row.appendChild(txt);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.className = 'btn-mini tight danger';
            rm.onclick = function () {
                catCfgInjectList.splice(idx, 1);
                renderInjectList(catCfgInjectList);
            };
            row.appendChild(rm);
            box.appendChild(row);
        })(i);
    }
}

// 前文 List 拖拽接线（每猫配置面）——拖动即改数组顺序，保存按钮落盘
enableDragSort('catCfgInject', function (from, to) {
    var moved = catCfgInjectList.splice(from, 1)[0];
    catCfgInjectList.splice(to, 0, moved);
    renderInjectList(catCfgInjectList);
});

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
// 默认猫配置入口——统一由多猫页 Majordomo 行「配置」按钮承担（openCatCfg 见 panel.js renderCatRow；F2.1 对话页签已移除，页面无独立 chatCfg 元素）

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
            row.draggable = true;
            row.title = '拖动调整顺序（保存后生效）';
            var handle = document.createElement('span');
            handle.textContent = '≡';
            handle.style.cssText = 'color:#6a6a6a;font-size:12px;cursor:move';
            row.appendChild(handle);
            var txt = document.createElement('span');
            txt.textContent = tplInjectList[idx];
            txt.style.cssText = 'flex:1;color:#6a9955;font-size:11px;word-break:break-all';
            row.appendChild(txt);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.className = 'btn-mini tight danger';
            rm.onclick = function () {
                tplInjectList.splice(idx, 1);
                renderTplInject();
            };
            row.appendChild(rm);
            box.appendChild(row);
        })(i);
    }
}

// 前文 List 拖拽接线（新猫默认模板面）
enableDragSort('tplInject', function (from, to) {
    var moved = tplInjectList.splice(from, 1)[0];
    tplInjectList.splice(to, 0, moved);
    renderTplInject();
});

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
            editBtn.className = 'btn-mini tight info';
            editBtn.onclick = function () {
                enterEditRoot(idx);
            };
            tdOp.appendChild(editBtn);
            var rm = document.createElement('button');
            rm.textContent = '移除';
            rm.className = 'btn-mini tight danger';
            rm.onclick = function () {
                // 移除确认——保存并重启后生效；未保存前可刷新页面恢复
                if (!window.confirm('移除受控根「' + rootsList[idx].id + '」？（保存并重启宿主后生效）')) { return; }
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
                idIn.className = 'input-mini';
                idIn.style.width = '90%';
                tdId.appendChild(idIn);
                tr.appendChild(tdId);
                var tdPath = document.createElement('td');
                var pathIn = document.createElement('input');
                pathIn.value = rootsList[rowIdx].path;
                pathIn.className = 'input-mini';
                pathIn.style.width = '96%';
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
                wSel.className = 'input-mini';
                tdW.appendChild(wSel);
                tr.appendChild(tdW);
                var tdOp = document.createElement('td');
                var okBtn = document.createElement('button');
                okBtn.textContent = '确定';
                okBtn.className = 'btn-mini primary tight';
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
                cancelBtn.className = 'btn-mini tight';
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
