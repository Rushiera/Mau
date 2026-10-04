// ═══════════════════════════════════════════
// chat/apirole.js —— 端点角色标签（侧翼件）
//
// 定位：顶栏 `[API 主要[模型] / 备用[模型]]` 标签的**唯一实现处**——只显当前生效站；点击手动对调（仅本会话有效）。
// 数据源：GET  /api/v1/api-role         → {"role":"主要|备用","primary":"主站模型名","backup":"备站模型名"}
// 写面：  POST /api/v1/api-role/toggle  → {"ok":true,"role":"主要|备用"}（清自动窗口：用户意志优先）
// 形态：DOM 槽位在 chat.html 顶栏（#chatApiRole），样式 chat.css「端点角色标签」段；本件零新增结构
// 边界：标签态与对调结果的真源在后端（会话级角色实例）——前端只渲染与投递，不本地推断、不本地翻转
// 失败面：读取失败回落占位并出声（不静默显示假值）；对调结果异步经视图帧回来（本件重读端点核对）
// ═══════════════════════════════════════════

/// 标签文本——`API 主要[模型名]`；角色非法 / 模型名缺失一律回落占位（不假装有值）
function apiRoleText(d) {
    if (!d || (d.role !== '主要' && d.role !== '备用')) {
        return '';
    }
    var model = (d.role === '备用') ? d.backup : d.primary;
    if (!model || model.length === 0) {
        return 'API ' + d.role + '[未配置]';
    }
    return 'API ' + d.role + '[' + model + ']';
}

/// 投影——文本 + 备用态类（备用 = 绿，与 API 池备用标记同色系）；元素缺失零动作
function apiRoleRender(d) {
    var node = document.getElementById('chatApiRole');
    if (!node) {
        return;
    }
    var text = apiRoleText(d);
    node.textContent = (text.length > 0) ? text : 'API —';
    if (d && d.role === '备用') {
        node.classList.add('backup');
    } else {
        node.classList.remove('backup');
    }
}

/// 读取——端点真源投影；失败出声（回落占位）
function apiRoleRefresh() {
    fetch('/api/v1/api-role')
        .then(function (r) { return r.json(); })
        .then(function (d) { apiRoleRender(d); })
        .catch(function (e) {
            apiRoleRender(null);
            if (typeof warn === 'function') { warn('端点角色读取失败', e); }
        });
}

/// 手动对调——投递后重读（真源在后端；不做本地翻转）
function apiRoleToggle() {
    fetch('/api/v1/api-role/toggle', { method: 'POST' })
        .then(function (r) { return r.json(); })
        .then(function () { apiRoleRefresh(); })
        .catch(function (e) {
            if (typeof warn === 'function') { warn('端点角色对调失败', e); }
        });
}

/// 接线——点击对调 + 首读；本件加载于页面尾部，DOM 已就绪
function apiRoleBind() {
    var node = document.getElementById('chatApiRole');
    if (!node) {
        return;
    }
    node.addEventListener('click', apiRoleToggle);
    apiRoleRefresh();
}

apiRoleBind();
