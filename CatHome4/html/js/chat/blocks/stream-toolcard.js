// ═══════════════════════════════════════════
// blocks/stream-toolcard.js —— toolcard.pending（live 类 · 进行中工具卡）
//
// 契约：
//   item = { type:'toolcard.pending', payload:{ name, arguments, order?, toolIndex?, toolTotal? } }
//   进行中判据 = payload.result === undefined（有 result 即终态，直接走 blocks/toolcard.js）
//
// 用法（两态）：
//   var h = buildStreamToolCard(payload);       // 先行卡（运行中）
//   replaceStreamToolCard(h, finalPayload);     // 终态到达——原位换卡（同一容器内替换）
//
// 产出：.chat-row.assistant.tool > details.chat-tool[open]
//
// 来源：chat-core.js chatOnLiveToolCard（第 452-475 行）+ chat-view.js chatToolCard（open=true 分支）
//
// 丢弃（属主干，不入素材）：
//   · 容器键表（viewContainers）与换卡配对逻辑 —— 新契约「前端零配对」，终态由主干按 live 镜像收敛
//   · 运行态类标记（chatSyncRunningMarks）、计时表（chatLiveMark / chatLiveSyncTimer / data-start）
//   · 保活（chatKeepAlive）、滚动跟随
// ═══════════════════════════════════════════

function buildStreamToolCard(payload) {
    // 进行中工具卡——展开态（两段式先行卡直接展示 ⏳ 占位行）
    var row = el('div', 'chat-row assistant tool');
    var card = buildToolCard(payload, true);
    row.appendChild(card);
    return { row: row, card: card };
}

function replaceStreamToolCard(h, payload) {
    // 终态换卡——原位替换（同一 details 位置），撤进行中外观
    // 新契约下终态卡与先行卡同为 toolcard 渲染，差别只在 result 有值与 open 缺省
    if (!h || !h.card || !h.card.parentNode) { return; }
    var next = buildToolCard(payload, false);
    h.card.parentNode.replaceChild(next, h.card);
    h.card = next;
    if (h.row) { h.row.classList.remove('pending'); }
}

/// live 区入口——payload.result 有值即终态（同一次调用，两态不是两条记录；LIVE_RENDERERS 消费）
function liveToolCardPending(payload) {
    var p = payload || {};
    var h = buildStreamToolCard(p);
    if (p.result !== undefined) {
        replaceStreamToolCard(h, p);
    }
    return h.row;
}
