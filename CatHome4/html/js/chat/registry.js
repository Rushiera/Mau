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
// 两族渲染函数的形态不同（见各素材头注）：
//   persist 族：function(payload) → 行元素（返回即完，挂载由 persist.js 承担）
//   live    族：build*() → 句柄 + 后续操作；本表用薄包装把句柄收敛为行元素
// ═══════════════════════════════════════════

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

/// 临时区渲染表——type → function(payload) → 行元素
var LIVE_RENDERERS = {
    'stream.text': liveStreamText,
    'stream.reason': liveStreamReason,
    'toolcard.pending': liveToolCardPending
};

/// 流式文本——live 区按帧全量镜像，故一次到位写入全文（增量模式留后续优化口）
function liveStreamText(payload) {
    var h = buildStreamText();
    setStreamText(h, (payload || {}).text || '');
    return h.row;
}

/// 流式思考——同上；素材只提供追加入口，句柄随帧重建故一次追加即等价全量
function liveStreamReason(payload) {
    var h = buildStreamReason();
    appendStreamReason(h, (payload || {}).text || '');
    return h.row;
}

/// 进行中工具卡——payload.result 有值即终态（同一次调用，两态不是两条记录）
function liveToolCardPending(payload) {
    var p = payload || {};
    var h = buildStreamToolCard(p);
    if (p.result !== undefined) {
        replaceStreamToolCard(h, p);
    }
    return h.row;
}
