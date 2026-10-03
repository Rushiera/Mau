// ═══════════════════════════════════════════
// lib/think-head.js —— 思考块头行统计（完成态 / 流式态共用）
// 来源：chat-think.js 头行统计段（chatThinkStats / chatThinkRate / chatThinkHeadText / chatThinkHeadFill）
// 口径：「Think · 1.2k 字符 · 34 行 · 12.3s · 96 字符/s」
//       · 时刻由调用方注入——ms 非数值 = 无统计来源（历史重建 / 刷新重建，载荷只有 text）→ 标「未统计」
//       · 已输出 < 1s 不出速率（分母无意义）；不做 token 换算（流式期前端无 token 数据）
// ═══════════════════════════════════════════

function thinkStats(text, ms) {
    // 统计取值——正文文本 → {chars, lines, ms}
    var t = (typeof text === 'string') ? text : '';
    return {
        chars: t.length,
        lines: (t.length === 0) ? 0 : t.split('\n').length,
        ms: (typeof ms === 'number' && ms >= 0) ? ms : null
    };
}

function thinkRate(chars, ms) {
    // 速率——字符 / 秒（取整）；已输出 < 1s 不显示（分母无意义）
    if (typeof ms !== 'number' || ms < 1000) { return ''; }
    return Math.round(chars / (ms / 1000)) + ' 字符/s';
}

function thinkHeadText(stats) {
    // 头行文本——单一拼装出口（流式态实时刷新 / 完成态一次成型共用）
    var s = stats || {};
    var parts = ['Think', fmtCount(s.chars || 0) + ' 字符', (s.lines || 0) + ' 行'];
    if (s.ms === null || s.ms === undefined) {
        parts.push('未统计');
    } else {
        parts.push(fmtMs(s.ms));
        var rate = thinkRate(s.chars || 0, s.ms);
        if (rate.length > 0) { parts.push(rate); }
    }
    return parts.join(' · ');
}

function thinkHeadFill(head, stats) {
    // 头行填充——标签与统计分色（文本来源唯一：thinkHeadText）
    if (!head) { return; }
    var text = thinkHeadText(stats);
    var cut = text.indexOf(' · ');
    head.textContent = '';
    head.appendChild(elText('span', 'ct-label', (cut < 0) ? text : text.substring(0, cut)));
    if (cut < 0) { return; }
    head.appendChild(elText('span', 'ct-stat', text.substring(cut)));
}
