// ═══════════════════════════════════════════
// chat/registry.js —— 渲染函数注册表（契约 design-ch4-protocol §12.5）
//
// 定位：块 type → 渲染函数的**唯一映射处**。前端唯一扩展点——新增块型 = 后端加一个
//       type + 本表加一行 + 一个渲染函数，其余不管。
//
// 契约：
//   persist item = { type, ts, msgIndex, round, payload }
//   live    item = { type, payload }
//
// 两族渲染函数形态（见各素材头注）：
//   persist 族：function(payload) → 行元素（返回即完，挂载由 persist.js 承担）
//   live    族：function(payload) → 行元素（一件式入口自各 live 件提供——A178 归位：
//               `build*()` → 句柄的中间形态留在件内，本表只登记入口名，不兼实现）
// ═══════════════════════════════════════════

// ── 形态声明（单一真相源 · 2026-10-03 莎定）────────────────────────
// 契约：对话区只有**两种行形态**——气泡（`.chat-bubble`）与朴素件（`.chat-plain`）。
// 形态是 **type 的特征**，不是区域（持久 / 临时）也不是持久性的特征：
//   · 气泡   = 会被人当「对话内容」读的块——用户输入（`user`）· LLM 最终输出（`text`）
//   · 朴素件 = 其余一切——思考 / 工具卡 / 错误 / 重试 / 注入报告 / 轮末结算 / 临时区流式件
// 🔴 本表是形态的唯一声明处：渲染函数一律经 `formClass()` 取类名，**不自持形态字符串**
//    （改形态 = 改本表一行，不动渲染函数）。
var BLOCK_FORM = {
    'user': 'bubble',
    'text': 'bubble',
    'reason': 'plain',
    'toolcard': 'plain',
    'retry': 'plain',
    'error': 'plain',
    'inject_report': 'plain',
    'roundsum': 'plain',
    'stream.text': 'plain',
    'stream.reason': 'plain',
    'toolcard.pending': 'plain'
};

/// 形态类名——表驱动唯一取用口；未声明的 type 回落朴素件（形态缺失不出气泡）
function formClass(type) {
    return (BLOCK_FORM[type] === 'bubble') ? 'chat-bubble' : 'chat-plain';
}

/// 持久区渲染表——type → function(payload) → 行元素
var PERSIST_RENDERERS = {
    'user': buildUserBlock,
    'text': buildTextBlock,
    'reason': buildReasonBlock,
    'toolcard': buildToolBlock,
    'retry': buildRetryBlock,
    'error': buildErrorBlock,
    'inject_report': buildInjectReportBlock,
    'roundsum': buildRoundSumBlock
};

/// 临时区渲染表——type → function(payload) → 行元素（各 live 件自提供一件式入口）
var LIVE_RENDERERS = {
    'stream.text': liveStreamText,
    'stream.reason': liveStreamReason,
    'toolcard.pending': liveToolCardPending
};
