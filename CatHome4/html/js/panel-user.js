// CH4 外观层——panel-user.js：当前用户态（侧栏项 + 双击弹层）——规格 design-ch4-user-state
// 数据面：GET/POST /api/v1/user-state（当前用户 + 档案候选 + QQ 非用户指令开关 + 群内身份清单）
// 依赖：app.js 先加载（全局工具就位）；本文件自持一次加载（不依赖 loadConfig 时序）
// 生效语义：QQ 消息头即时生效；注入段（猫的对话身份）新会话重新注入后生效

var userModalEl = document.getElementById('userModal');
var sidebarUserEl = document.getElementById('sidebarUser');
var userStateData = { current: '', acceptNonUser: false, qqIds: '', users: [] };

// 用户态加载——侧栏项回填 + 弹层数据面（失败静默降级：侧栏保留初值，不阻断面板）
function loadUserState() {
    fetch('/api/v1/user-state')
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d || !d.ok) { return; }
            userStateData.current = d.current || '';
            userStateData.acceptNonUser = !!d.acceptNonUser;
            userStateData.qqIds = d.qqIds || '';
            userStateData.users = d.users || [];
            applyUserSidebar();
        })
        .catch(function () { /* 宿主未运行——保留初值 */ });
}

// 侧栏项回填——名字 + 悬停提示
function applyUserSidebar() {
    if (!sidebarUserEl) { return; }
    sidebarUserEl.textContent = userStateData.current || '—';
    sidebarUserEl.title = '当前用户：' + (userStateData.current || '—') + '（双击切换）';
}

// 弹层渲染——候选单选 + 群内身份 + 非用户指令开关
function renderUserModal() {
    var box = document.getElementById('userModalList');
    box.textContent = '';
    for (var i = 0; i < userStateData.users.length; i++) {
        var u = userStateData.users[i];
        var label = document.createElement('label');
        label.style.cssText = 'display:flex;align-items:flex-start;gap:6px;font-size:var(--ch-fs-tag);color:var(--ch-fg-muted);padding:5px 6px;border:1px solid var(--ch-line);border-radius:4px;margin-bottom:5px;cursor:pointer';
        var radio = document.createElement('input');
        radio.type = 'radio';
        radio.name = 'userCurrent';
        radio.value = u.name;
        if (u.name === userStateData.current) { radio.checked = true; }
        label.appendChild(radio);
        var textWrap = document.createElement('span');
        var nameEl = document.createElement('span');
        nameEl.style.color = 'var(--ch-identity)';
        nameEl.textContent = u.name;
        textWrap.appendChild(nameEl);
        if (u.desc) {
            var descEl = document.createElement('span');
            descEl.style.color = 'var(--ch-fg-weak)';
            descEl.textContent = '　' + u.desc;
            textWrap.appendChild(descEl);
        }
        label.appendChild(textWrap);
        box.appendChild(label);
    }
    document.getElementById('userQqIds').value = userStateData.qqIds || '';
    document.getElementById('userQqAccept').checked = !!userStateData.acceptNonUser;
    document.getElementById('userModalMsg').textContent = '';
}

// 弹层开合
function openUserModal() {
    renderUserModal();
    userModalEl.style.display = 'flex';
}

function closeUserModal() {
    userModalEl.style.display = 'none';
}

// 保存——单选 + 群内身份 + 开关一并提交（字段级写；保存后回填侧栏）
function saveUserState() {
    var picked = '';
    var radios = document.querySelectorAll('#userModalList input[name="userCurrent"]');
    for (var i = 0; i < radios.length; i++) {
        if (radios[i].checked) { picked = radios[i].value; }
    }
    if (picked.length === 0) {
        document.getElementById('userModalMsg').textContent = '请选择用户';
        return;
    }
    var payload = {
        current: picked,
        acceptNonUser: document.getElementById('userQqAccept').checked,
        qqIds: document.getElementById('userQqIds').value
    };
    fetch('/api/v1/user-state', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(function (r) { return r.json(); })
        .then(function (d) {
            if (!d || !d.ok) {
                document.getElementById('userModalMsg').textContent = '保存失败: ' + ((d && d.error) || '未知错误');
                return;
            }
            userStateData.current = d.current || picked;
            userStateData.acceptNonUser = !!d.acceptNonUser;
            userStateData.qqIds = d.qqIds || '';
            applyUserSidebar();
            document.getElementById('userModalMsg').textContent = '已保存——QQ 消息头即时生效；注入段新会话生效';
        })
        .catch(function (err) { document.getElementById('userModalMsg').textContent = '请求失败: ' + err; });
}

// 接线——侧栏双击开弹层；关闭 / 取消 / 遮罩点击关闭；保存
if (sidebarUserEl) {
    sidebarUserEl.addEventListener('dblclick', openUserModal);
}
if (userModalEl) {
    document.getElementById('userModalClose').addEventListener('click', closeUserModal);
    document.getElementById('userModalCancel').addEventListener('click', closeUserModal);
    document.getElementById('userModalSave').addEventListener('click', saveUserState);
    userModalEl.addEventListener('click', function (e) {
        if (e.target === userModalEl) { closeUserModal(); }
    });
}
loadUserState();
