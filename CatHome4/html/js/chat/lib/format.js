// ═══════════════════════════════════════════
// lib/format.js —— 数字与时长格式化
// 来源：chat-view.js chatFmtCount（第 6-12 行）· chatFmtMs（第 428-445 行）
// 取舍：原样保留（纯函数，无耦合）——去掉 chat 前缀
// ═══════════════════════════════════════════

// 计数格式化——统计数字统一入口（A201：token / 字符数 / 用量显示位共用本函数）
// 口径：一律两位小数；≥1000 → x.xx K；≥1000000 → x.xx M（M 为最大单位，不再进位）
// 不入此列：离散计数（前文条数 / 工具次数 / 请求次数）——按整数直出
function fmtCount(n) {
    var v = Number(n) || 0;
    if (v >= 1000000) { return (v / 1000000).toFixed(2) + 'M'; }
    if (v >= 1000) { return (v / 1000).toFixed(2) + 'K'; }
    return v.toFixed(2);
}

// 数字分片（2026-10-06 · 莎定）——整数部分与小数部分独立渲染：小数（含小数点）包 `.num-frac`（同文字灰 + 小 1px）
// 口径：输入 = 已格式化数字串（`fmtCount` 产物，允许 K/M 单位与 % 等尾缀）；无小数段则原样返回
// 消费面：信息位（`chat/state/info.js`）· 轮末结算（`blocks/roundsum.js`）——字面量输入无用户文本，返回片段可直插
// @param {string} [tailCls] 尾缀（K/M/% 等）包裹类名——省略时尾缀直出（继承容器色）
function fmtNumHtml(s, tailCls) {
    var str = String(s == null ? '' : s);
    var m = /^([0-9]+)(\.[0-9]+)?(.*)$/.exec(str);
    if (m == null) {
        return str;
    }
    var tail = m[3];
    if (tailCls && tail.length > 0) {
        tail = '<span class="' + tailCls + '">' + tail + '</span>';
    }
    if (m[2] === undefined) {
        return m[1] + tail;
    }
    return m[1] + '<i class="num-frac">' + m[2] + '</i>' + tail;
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
