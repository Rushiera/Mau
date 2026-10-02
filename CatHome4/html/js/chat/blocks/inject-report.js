// ═══════════════════════════════════════════
// blocks/inject-report.js —— inject_report 块（persist 类 · 前文加载报告）
//
// 契约：
//   item = { type:'inject_report', ts, msgIndex, round,
//            payload:{ files:[{file, status, chars?, message?}], total, ok, missing, failed, toolGroups:[{group, tools:[{name}]}] } }
//   status = ok / missing / error 三态；工具组段为「已启用 N 个工具」明细
//
// 产出：.chat-row.assistant.inject > .chat-plain.inject > .inject-report（形态见 registry.js §形态声明）
//
// 来源：chat-view.js chatInjectReportHtml（第 268-300 行）
//
// 取舍：报告结构原样保留；文本转义统一走 mdEscapeHtml（lib/md.js 唯一转义入口），替代旧全局 escapeHtml
// ═══════════════════════════════════════════

function injectReportHtml(p) {
    // 注入报告 HTML——新会话前文加载明细（ok/missing/error 三态 + 字符数 + 注入工具组）
    var d = p || {};
    var files = d.files || [];
    var total = d.total || 0;
    var ok = d.ok || 0;
    var missing = d.missing || 0;
    var failed = d.failed || 0;
    var html = '<div class="inject-report">'
        + '<div class="ir-head">📚 前文加载：' + ok + '/' + total + ' 成功'
        + (missing > 0 ? ' · 缺失 ' + missing : '')
        + (failed > 0 ? ' · 失败 ' + failed : '')
        + '</div>';
    if (files.length > 0) {
        html += '<div class="ir-list">';
        for (var i = 0; i < files.length; i++) {
            var f = files[i];
            var icon = '✅';
            var cls = 'ok';
            if (f.status === 'missing') { icon = '⚠️'; cls = 'missing'; }
            else if (f.status === 'error') { icon = '❌'; cls = 'error'; }
            html += '<div class="ir-item ' + cls + '">' + icon + ' ' + mdEscapeHtml(f.file || '')
                + (f.status === 'ok' && f.chars > 0 ? '（' + f.chars + ' 字符）' : '')
                + (f.message ? ' — ' + mdEscapeHtml(f.message) : '')
                + '</div>';
        }
        html += '</div>';
    }
    // 注入工具组——独立分区（组名 + 工具名列表；desc 不进展示）
    var groups = d.toolGroups || [];
    if (groups.length > 0) {
        var toolCount = 0;
        for (var g = 0; g < groups.length; g++) { toolCount = toolCount + (groups[g].tools || []).length; }
        html += '<div class="ir-tools"><div class="ir-tools-head">🛠️ 已启用 ' + toolCount + ' 个工具</div>';
        for (var g2 = 0; g2 < groups.length; g2++) {
            var names = [];
            var tools = groups[g2].tools || [];
            for (var t = 0; t < tools.length; t++) { names.push(tools[t].name); }
            html += '<div class="ir-tool-row"><span class="ir-tool-group">'
                + mdEscapeHtml(groups[g2].group || '内置') + '</span> —— '
                + mdEscapeHtml(names.join('、')) + '</div>';
        }
        html += '</div>';
    }
    html += '</div>';
    return html;
}

function buildInjectReportBlock(payload) {
    // 注入报告块——前文加载明细（独立持久化字段，重建不清）
    var row = el('div', 'chat-row assistant inject');
    var bubble = el('div', formClass('inject_report') + ' inject');
    bubble.innerHTML = injectReportHtml(payload);
    row.appendChild(bubble);
    return row;
}
