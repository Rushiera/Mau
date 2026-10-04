// ═══════════════════════════════════════════
// chat/blocks/tool-run.js —— toolrun（live 类 · 工具运行）
//
// 契约（design-ch4-protocol §12.4 / §12.5）：
//   item = { type:'toolrun', payload:{ name, arguments, order, toolIndex, toolTotal, result?, durMs } }
//   载荷有 result = 结果态、无 = 进行中态——**同一渲染函数的载荷分支**（外观层合法推算，非生命周期判态，A187）。
//
// 纯渲染（A187）：payload → 行元素一次成型（工具卡骨架共用 blocks/toolcard.js 的 buildToolCard）；
//
// 退役（A187）：旧件 buildStreamToolCard / replaceStreamToolCard 句柄式换卡——
//   全量镜像路径每帧重建行元素，换卡本就是空动作。
//
// 产出：.chat-row.assistant.tool > details.chat-tool（展开态由载荷 result 有无决定）
// ═══════════════════════════════════════════

/// live 段入口——toolrun：进行中 / 结果态同一渲染（registry.js 的 LIVE_RENDERERS 消费）
function liveToolRun(payload) {
    var p = payload || {};
    var row = blockRow('toolrun');
    row.appendChild(buildToolCard(p, p.result === undefined));
    return row;
}
