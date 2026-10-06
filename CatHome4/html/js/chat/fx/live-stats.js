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
// 刷新：**单一 100ms 心跳**（10 次/秒）——三行同一节拍（2026-10-06 莎定：原「机制钩子逐帧 + 500ms
//   心跳」两路合并为一路）。机制侧**不推送**（`stream.js` 只提供取值口），本件按自身节拍轮询——
//   读数按 10 次/秒刷新已够跟手，且省掉每帧写 DOM；打字机的连续观感由面板正文自身承担。
// ═══════════════════════════════════════════

/// 刷新节拍（毫秒）——10 次/秒；line / char / spd 三行同此节拍
var LIVE_STATS_TICK_MS = 100;

/// 上次写入的文案——相同即不碰 DOM（节拍级去抖）
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

/// 刷新——按节拍取值：文案变化才写 DOM；无活跃段（或元素缺失）即隐藏
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

/// 启动入口（`fx/registry.js` 表内 init）——初态刷新 + 100ms 心跳（本件唯一刷新驱动）
function liveStatsInit() {
    liveStatsRefresh();
    if (typeof setInterval === 'function') {
        setInterval(liveStatsRefresh, LIVE_STATS_TICK_MS);
    }
}
