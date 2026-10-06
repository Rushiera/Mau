// ═══════════════════════════════════════════
// blocks/roundsum.js —— roundsum 块（persist 类 · 轮末统计）
//
// 契约：
//   item = { type:'roundsum', ts, msgIndex, round,
//            payload:{ data:{ prompt, completion, cacheHit, miss?, toolCount, requests?, elapsedMs,
//                             phases:{ link, wait, think, tool, run, reply } } } }
//   口径三级（会话 / 轮 / 请求）——本块只承载「轮」级：本轮全部请求的累加
//   平均首 token 延迟 = phases.link 累计 ÷ requests（缺 link 不加后缀）
//
// 产出：.chat-row.assistant.roundsum > .chat-plain.roundsum > .rs-head + .rs-tok + .rs-tools + .rs-times（形态见 registry.js §形态声明）
//
// 来源：chat-view.js chatOnRoundSum（第 447-494 行）
//
// 取舍：三段结构与文案原样保留；丢弃气泡构造（chatBubble → el）与 DOM 挂载 / 滚动跟随
// ═══════════════════════════════════════════

// 六态用时标签（idle 不计时故不入载荷）——单点声明在 registry.js 的 RUN_PHASES（A179 收口）

function roundsumHtml(payload) {
    var d = (payload && payload.data) || {};
    var phases = d.phases || {};
    // miss 为后端派生值（§12.10 口径：miss = prompt − cacheHit）——前端零兜底，字段缺失即显 0
    var miss = d.miss || 0;

    var html = '<div class="rs-head">📊 Round</div>';

    // cache 命中率——cacheHit / prompt（prompt=0 时 0%）
    var promptTotal = d.prompt || 0;
    var hitRate = (promptTotal > 0) ? ((d.cacheHit || 0) / promptTotal * 100) : 0;
    html += '<div class="rs-tok">↑' + fmtCount(promptTotal)
        + ' ↓' + fmtCount(d.completion || 0)
        + ' cache ' + fmtCount(d.cacheHit || 0)
        + ' miss ' + fmtCount(miss)
        + ' 🎯' + hitRate.toFixed(1) + '%</div>';

    // 第二行——工具次数 · 请求次数 · All 总耗时（三段各自着色）
    var toolsLine = '';
    if (d.toolCount > 0) { toolsLine = '<span class="rs-tool">🔧 Tool ' + d.toolCount + '</span>'; }
    if (d.requests !== undefined) {
        var apiText = '🔄 Api ' + (d.requests || 0);
        var linkMs = phases.link || 0;
        if (d.requests > 0 && linkMs > 0) {
            apiText += '（' + (linkMs / 1000 / d.requests).toFixed(2) + ' s /use）';
        }
        toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-api">' + apiText + '</span>';
    }
    toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-all">⏱ All ' + fmtMs(d.elapsedMs) + '</span>';
    html += '<div class="rs-tools">' + toolsLine + '</div>';

    // 第三行——六态用时（非零态上尾巴；单色弱化）
    var tparts = [];
    for (var pi = 0; pi < RUN_PHASES.length; pi++) {
        var pv = phases[RUN_PHASES[pi].key] || 0;
        if (pv > 0) { tparts.push(RUN_PHASES[pi].label + ' ' + fmtMs(pv)); }
    }
    html += '<div class="rs-times">' + tparts.join(' · ') + '</div>';
    return html;
}

function buildRoundSumBlock(payload) {
    // 轮末统计气泡——弱化系统样式；历史重建与实时推送共用同一渲染面
    var row = blockRow('roundsum');
    var bubble = el('div', bodyClass('roundsum'));
    bubble.innerHTML = roundsumHtml(payload);
    row.appendChild(bubble);
    return row;
}
