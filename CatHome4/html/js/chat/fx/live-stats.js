// ═══════════════════════════════════════════
// chat/fx/live-stats.js —— 流式统计行（独立功能面 · 契约 §12.8）
//
// 定位：切换开关**正上方**的灰色统计行（2026-10-06 莎定）——三行 `label: value`：
//   `line: <行数>` / `char: <字符数>` / `spd: <速度>/s`。
//
// 输入源（只读）：`stream.js::streamStats()`——流式段的原始事实（类型 / 字符数 / 行数 / 段用时）。
//   本件只做**显示推算**（速度 = 字符数 ÷ 段用时），零业务效果、零写动作 → 独立功能面（§12.8 三判据）。
// 输出面：`#chatLiveStats`（无活跃流式段即隐藏——不占位）。
//
// 口径：
//   · 字符数 / 行数 = **整数直出**（实时读数，不进 `fmtCount` 的两位小数口径——与「离散计数直出」同族）
//   · 速度 = 字符数 ÷ 段用时，一位小数；**段用时 < 1s 显示 —**（分母无意义——与思考块完成态口径一致）
//   · 行数口径 = 段内换行数 + 1（空段不显示行数：0）
//
// 刷新两路：① 机制侧钩子（`stream.js::streamNotifyStats` —— 内容变化即时同步）
//          ② 本件 500ms 心跳（文字静止时速度读数仍随时间走，不冻在旧值）
// ═══════════════════════════════════════════

/// 心跳周期（毫秒）——内容静止时维持速度读数
var LIVE_STATS_TICK_MS = 500;

/// 上次写入的文案——相同即不碰 DOM（帧级调用去抖）
var liveStatsLast = '';

/// 统计行元素（缺失即零动作——防御式）
function liveStatsEl() {
    return document.getElementById('chatLiveStats');
}

/// 文案组装——三行 `label: value`；无活跃段 → 空串（隐藏）
function liveStatsText(st) {
    if (!st) {
        return '';
    }
    var spd = '—';
    if (st.elapsedMs >= 1000) {
        spd = (st.chars / (st.elapsedMs / 1000)).toFixed(1) + '/s';
    }
    return 'line: ' + st.lines + '\nchar: ' + st.chars + '\nspd: ' + spd;
}

/// 刷新——文案变化才写 DOM；无活跃段（或元素缺失）即隐藏
function liveStatsRefresh() {
    var el = liveStatsEl();
    if (!el) {
        return;
    }
    var st = null;
    if (typeof streamStats === 'function') {
        st = streamStats();
    }
    var text = liveStatsText(st);
    if (text !== liveStatsLast) {
        liveStatsLast = text;
        el.textContent = text;
    }
    // 显隐独立于文案去抖——初态即无活跃段时也要落到隐藏（元素初始 display 为空串）
    var want = (text.length === 0) ? 'none' : '';
    if (el.style.display !== want) {
        el.style.display = want;
    }
}

/// 启动入口（`fx/registry.js` 表内 init）——初态刷新 + 心跳
function liveStatsInit() {
    liveStatsRefresh();
    if (typeof setInterval === 'function') {
        setInterval(liveStatsRefresh, LIVE_STATS_TICK_MS);
    }
}
