// CH4 外观层——ui-common.js：两页面共用前端工具（版本标记 + ui.* 偏好接线）——消除 chat.html/index.html 内联副本
// 加载顺序：本文件先于 app.js / chat-core.js（index.html 由 app.js 段12 复用 applyUiConfig；chat.html 用 loadUiConfig 独立接线）
// 职责：①loadUiVersion——data-version 组件填充（frontend-version.json）②applyUiConfig——ui.* 配置 → CSS 变量
//       ③loadUiConfig——拉 /api/v1/config 并接线（无配置表的页面用；index.html 由 app.js loadConfig 复用同一响应）

function loadUiVersion() {
    // 前端版本自动化——data-version 组件统一填充（js/frontend-version.json——Vitest 每次运行自动递增；两外观层同函数同文件）
    fetch('js/frontend-version.json')
        .then(function (r) { return r.json(); })
        .then(function (v) {
            var txt = 'UI v' + v.version + ' | ' + (v.time || '');
            var els = document.querySelectorAll('[data-version]');
            for (var i = 0; i < els.length; i = i + 1) { els[i].textContent = txt; }
        })
        .catch(function () { /* 兜底——宿主未运行/文件缺失：保持占位 UI v? 静默降级 */ });
}

/**
 * ui.* 字号缩放配置 → CSS 变量（读配置覆盖 tokens.css 默认；读不到或越界一律按默认 100）
 * @param {Array} items 配置项列表（/api/v1/config 的 items 段；缺失按默认 100 处理）
 */
function applyUiConfig(items) {
    var scale = 100;
    for (var i = 0; i < (items || []).length; i++) {
        if (items[i].key === 'ui.font_scale') { var v = parseInt(items[i].value, 10); if (v >= 50 && v <= 300) { scale = v; } }
    }
    document.documentElement.style.setProperty('--ch-fs-scale', String(scale / 100));
}

function loadUiConfig() {
    // 无配置表页面的独立接线入口（chat.html——对话页无配置区）
    fetch('/api/v1/config')
        .then(function (r) { return r.json(); })
        .then(function (d) { applyUiConfig(d.items || []); })
        .catch(function () { /* 兜底——宿主未运行：保持 CSS 默认字号 */ });
}

/**
 * 文本安全转义（& < >）——聊天内容/Note 任务/待发送队列渲染共用（XSS 与格式双防）
 * 归属调整（2026-09-11）：原 chat-core.js 私有 → 公共（消除 chat-view/chat-note 对 core 的隐式跨文件依赖）
 * @param {string} s 原始文本
 * @returns {string} 转义后文本（可安全拼入 innerHTML 内容位）
 */
function escapeHtml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

/**
 * 失败可见化——替换空 catch（不再静默吞异常）；console 记录 + 主面板顶部状态位提示（无提示位的页面仅 console）
 * @param {string} scope 失败动作（如 '初始快照'）
 * @param {*} err 错误对象或描述
 */
function uiWarn(scope, err) {
    var msg = '[UI] ' + scope + '失败: ' + ((err && err.message) ? err.message : String(err));
    if (typeof console !== 'undefined' && console.warn) { console.warn(msg); }
    var el = document.getElementById('meta');
    if (el) { el.textContent = msg; }
}

/**
 * 行内编辑接线——change 三态分流：未改动 / 清空 / 改动
 * 未改动（去空白后等于原值）与清空（去空白后为空）都算「不改动」——清空回落原值，两者都不提交；
 * 只有真改动才调 onCommit（新值已去空白）。判据来源：配置页行内编辑轮（API 池 / QQ Bot 池 / 默认前文）
 * @param {HTMLInputElement} inp 行内输入框（初值 = 原值）
 * @param {string} origValue 原值（对照面）
 * @param {function(string)} onCommit 真改动时提交（参数 = 去空白后的新值）
 */
function wireInlineEdit(inp, origValue, onCommit) {
    inp.addEventListener('change', function () {
        var v = inp.value.trim();
        if (v === origValue) { return; }
        if (v.length === 0) { inp.value = origValue; return; }
        onCommit(v);
    });
}

