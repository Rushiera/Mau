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
 * ui.* 字号配置 → CSS 变量（P8.5d 接线——读配置覆盖 CSS 默认；读不到默认 14px；全局字号配置已移除——静态提档替代 2026-08-28）
 * @param {Array} items 配置项列表（/api/v1/config 的 items 段；缺失按默认 14px 处理）
 */
function applyUiConfig(items) {
    var chat = 14;
    for (var i = 0; i < (items || []).length; i++) {
        if (items[i].key === 'ui.chat_font_size') { var c = parseInt(items[i].value, 10); if (c > 0) { chat = c; } }
    }
    document.documentElement.style.setProperty('--chat-font-size', chat + 'px');
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
