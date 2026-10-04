// ═══════════════════════════════════════════
// lib/format.js —— 数字与时长格式化
// 来源：chat-view.js chatFmtCount（第 6-12 行）· chatFmtMs（第 428-445 行）
// 取舍：原样保留（纯函数，无耦合）——去掉 chat 前缀
// ═══════════════════════════════════════════

// 计数格式化——大数转 k/M（≥1000 → x.xx k；≥1000000 → x.xx M；M 为最大单位；保留两位小数）
function fmtCount(n) {
    var v = Number(n) || 0;
    if (v >= 1000000) { return (v / 1000000).toFixed(2) + 'M'; }
    if (v >= 1000) { return (v / 1000).toFixed(2) + 'k'; }
    return String(v);
}

// 时长格式化——毫秒 → 可读（<60s → x.xs；≥60s → xm x.xs；≥3600s → xh xm x.xs——hour 最高单位不再进位）
function fmtMs(ms) {
    var s = (ms || 0) / 1000;
    if (s >= 3600) {
        var hours = Math.floor(s / 3600);
        var rest = s - hours * 3600;
        var mins = Math.floor(rest / 60);
        var secs = rest - mins * 60;
        return hours + 'h' + mins + 'm' + secs.toFixed(1) + 's';
    }
    if (s >= 60) {
        var mins2 = Math.floor(s / 60);
        var secs2 = s - mins2 * 60;
        return mins2 + 'm' + secs2.toFixed(1) + 's';
    }
    return s.toFixed(1) + 's';
}
