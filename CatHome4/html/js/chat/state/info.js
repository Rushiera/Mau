// ═══════════════════════════════════════════
// chat/state/info.js —— 顶栏信息位投影（A194 自 state.js 迁出）
//
// 输入：state 段 `tokens`（`count` 前文条数 · `context` 前文长度 · `session*` 会话级消耗三项）
//       `meta`（`contextChars` 前文字符数）
// 输出：#chatInfo——「前文 N 条 X.XXK token（Y.YYK字符）| （🎯…%）Hit… Miss…  Down…」（经信息位单点 `chatInfoSet`）
// 判据：只读自己声明的字段、只写自己的输出面（对应 `state/registry.js::STATE_DECL` 的 `info` 行）
// 归位（A179）：告警 / 提示不走此处（→ 通知面 `chatPetSay` 桌宠气泡）——本条信息位是 state 段专属
// 格式化（A201）：统计数字一律走 `fmtCount`（两位小数 + K/M 进位）；条数为离散计数，整数直出
// ═══════════════════════════════════════════

/// 头部信息位——前文条数与长度、前文字符数、会话级（全局）token 消耗与命中率
/// 富文本分片（2026-10-06）：数值包 `.ci-num`（随正文高亮色）· 命中率包 `.ci-rate`（淡紫）；
///   小数分片（2026-10-06 · 莎定）：整数与小数独立渲染——小数（含小数点）包 `.num-frac`（同文字灰 + 小 1px，
///   含百分比小数；单位 K/M 与 % 留在整数侧）
///   标签分片（2026-10-06 · 莎定）：Hit（命中量 = 输入总量 − 未命中）包 `.ci-hit`（淡蓝）· Miss 包 `.ci-miss`（橙黄）· Down 包 `.ci-down`（淡红）——各 +1px
///   片段 = 固定字面量 + `fmtCount` 数值（无用户文本）——组装侧无需转义
/// @param {object} st state 段整段（分发器传入；只读 tokens / meta）
function stateRenderInfo(st) {
    var t = st.tokens || {};
    var m = st.meta || {};
    var num = function (v) {
        return '<span class="ci-num">' + fmtNumHtml(fmtCount(v)) + '</span>';
    };
    // 条数为离散计数——整数直出（不走 fmtCount）；其余统计走 num() 分片
    // 命中量 = 输入总量 − 未命中量（展示层组合——state 段暂无 hit 字段；命中率仍取后端 rate 原值，不重算）
    var hit = (Number(t.sessionPrompt) || 0) - (Number(t.sessionMiss) || 0);
    if (hit < 0) {
        hit = 0;
    }
    // 会话级四项（2026-10-06 · 莎定）——顺序按计费从高到低：命中率 → Hit（命中量）→ Miss（未命中）→ Down（输出 completion）
    // 间隔：命中率 / Hit / Miss 之间各一空格 · Miss 与 Down 之间两空格；标签首字母大写 + 比常规大 1px（见 css）
    var html = '前文 <span class="ci-num">' + (t.count || 0) + '</span>'
        + ' 条 ' + num(t.context) + ' token（' + num(m.contextChars) + '字符）'
        + '| （🎯<span class="ci-rate">' + fmtNumHtml((Number(t.sessionRate || 0) * 100).toFixed(2) + '%') + '</span>）'
        + ' <span class="ci-hit">Hit</span>' + num(hit)
        + ' <span class="ci-miss">Miss</span>' + num(t.sessionMiss)
        + '  <span class="ci-down">Down</span>' + num(t.sessionCompletion);
    if (typeof chatInfoSet === 'function') {
        chatInfoSet(html);
    }
}
