// CH4 外观层——panel-appearance.js：外观配置池管理（config 页签，A244）——panel-apis.js 同构
// 依赖：app.js + panel.js 先加载（全局状态已就位）；本文件只承载外观池的列表/新建/编辑/删除/设默认
// 一套外观 = 桌宠种类 + TTS 风格 + 轮末总结 + 总结成音频（四字段同属一条目）

// [段1] 外观配置池管理
var apcTableBody = document.querySelector('#apcTable tbody');
var apcMsgEl = document.getElementById('apcMsg');
var editingApcId = '';

function loadAppearances() {
    fetch('/api/v1/appearance-apis')
        .then(function (r) { return r.json(); })
        .then(function (d) { renderAppearances(d.items || []); })
        .catch(function () { apcMsgEl.textContent = '外观池加载失败——宿主未运行？'; });
}

function renderAppearances(items) {
    apcTableBody.textContent = '';
    apcMsgEl.textContent = items.length + ' 套外观';
    for (var i = 0; i < items.length; i++) {
        renderAppearanceRow(items[i]);
    }
}

function renderAppearanceRow(apc) {
    var tr = document.createElement('tr');
    var tdName = document.createElement('td');
    tdName.textContent = apc.displayName || '';
    tdName.style.color = 'var(--ch-identity)';
    if (apc.isDefault) { tdName.textContent = '★ ' + tdName.textContent; }
    tr.appendChild(tdName);
    var tdId = document.createElement('td');
    tdId.textContent = apc.appearanceId || '';
    tdId.style.color = 'var(--ch-fg-weak)';
    tdId.style.fontSize = '10px';
    tdId.style.wordBreak = 'break-all';
    tr.appendChild(tdId);
    var tdPet = document.createElement('td');
    tdPet.textContent = apc.petKind ? apc.petKind : '（默认素材）';
    if (!apc.petKind) { tdPet.style.color = 'var(--ch-fg-weak)'; }
    tr.appendChild(tdPet);
    var tdTts = document.createElement('td');
    tdTts.textContent = apc.ttsStyle ? apc.ttsStyle : '（无）';
    if (!apc.ttsStyle) { tdTts.style.color = 'var(--ch-fg-weak)'; }
    tr.appendChild(tdTts);
    var tdSummary = document.createElement('td');
    tdSummary.textContent = apc.roundSummary ? '开' : '关';
    tdSummary.style.color = apc.roundSummary ? 'var(--ch-ok-weak)' : 'var(--ch-fg-weak)';
    tr.appendChild(tdSummary);
    var tdAudio = document.createElement('td');
    tdAudio.textContent = apc.summaryAudio ? '开' : '关';
    tdAudio.style.color = apc.summaryAudio ? 'var(--ch-ok-weak)' : 'var(--ch-fg-weak)';
    tr.appendChild(tdAudio);
    var tdOp = document.createElement('td');
    tdOp.style.whiteSpace = 'nowrap';
    if (!apc.isDefault) {
        var defBtn = document.createElement('button');
        defBtn.textContent = '设为默认';
        defBtn.className = 'btn-mini accent';
        defBtn.onclick = function () { setDefaultAppearance(apc); };
        tdOp.appendChild(defBtn);
    }
    var editBtn = document.createElement('button');
    editBtn.textContent = '编辑';
    editBtn.className = 'btn-mini';
    editBtn.onclick = function () { startAppearanceEdit(apc); };
    tdOp.appendChild(editBtn);
    var delBtn = document.createElement('button');
    delBtn.textContent = '删除';
    delBtn.className = 'btn-mini danger';
    delBtn.onclick = function () {
        if (!confirm('删除外观「' + apc.displayName + '」？引用它的猫将回退默认外观。')) { return; }
        fetch('/api/v1/appearance-apis/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ appearanceId: apc.appearanceId })
        })
            .then(function (r) { return r.json(); })
            .then(function (d) {
                apcMsgEl.textContent = d.ok ? '已删除' : '删除失败: ' + d.error;
                loadAppearances();
            })
            .catch(function (e) {
                apcMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
            });
    };
    tdOp.appendChild(delBtn);
    tr.appendChild(tdOp);
    apcTableBody.appendChild(tr);
}

function startAppearanceEdit(apc) {
    editingApcId = apc.appearanceId;
    document.getElementById('apcNewName').value = apc.displayName || '';
    document.getElementById('apcNewPet').value = apc.petKind || '';
    document.getElementById('apcNewTts').value = apc.ttsStyle || '';
    document.getElementById('apcNewSummary').checked = !!apc.roundSummary;
    document.getElementById('apcNewAudio').checked = !!apc.summaryAudio;
    document.getElementById('apcAddBtn').textContent = '保存';
    document.getElementById('apcCancelEdit').style.display = '';
    apcMsgEl.textContent = '编辑中: ' + apc.displayName;
}

function cancelAppearanceEdit() {
    editingApcId = '';
    document.getElementById('apcNewName').value = '';
    document.getElementById('apcNewPet').value = '';
    document.getElementById('apcNewTts').value = '';
    document.getElementById('apcNewSummary').checked = false;
    document.getElementById('apcNewAudio').checked = false;
    document.getElementById('apcAddBtn').textContent = '新建';
    document.getElementById('apcCancelEdit').style.display = 'none';
    apcMsgEl.textContent = '';
}

function appearanceSubmit() {
    var payload = {
        displayName: document.getElementById('apcNewName').value.trim(),
        petKind: document.getElementById('apcNewPet').value.trim(),
        ttsStyle: document.getElementById('apcNewTts').value.trim(),
        roundSummary: document.getElementById('apcNewSummary').checked,
        summaryAudio: document.getElementById('apcNewAudio').checked
    };
    var url = '/api/v1/appearance-apis';
    if (editingApcId.length > 0) {
        url = '/api/v1/appearance-apis/edit';
        payload.appearanceId = editingApcId;
    }
    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apcMsgEl.textContent = d.ok ? (editingApcId.length > 0 ? '已保存' : '已新建') : '失败: ' + d.error;
            cancelAppearanceEdit();
            loadAppearances();
        })
        .catch(function (e) {
            apcMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

// 设为默认——POST /api/v1/appearance-apis/default（appearanceId 零值的猫固定走它；切换立即生效）
function setDefaultAppearance(apc) {
    fetch('/api/v1/appearance-apis/default', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ appearanceId: apc.appearanceId })
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            apcMsgEl.textContent = d.ok ? '默认外观已切换: ' + apc.displayName : '失败: ' + d.error;
            loadAppearances();
        })
        .catch(function (e) {
            apcMsgEl.textContent = '请求失败: ' + e.message + '（宿主未运行或端点不存在？）';
        });
}

document.getElementById('apcAddBtn').onclick = appearanceSubmit;
document.getElementById('apcRefresh').onclick = loadAppearances;
document.getElementById('apcCancelEdit').onclick = cancelAppearanceEdit;
loadAppearances();
