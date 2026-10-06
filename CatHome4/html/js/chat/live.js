// ═══════════════════════════════════════════
// chat/live.js —— 临时区（契约 §12.5）
//
// 定位：临时区的唯一应用入口。**状态投影**——后端只给两个字符串（type + context），
//   每次收帧整体替换面板内容，不比对、不 diff、不合并。
//
// 物理位置：输入区内、与输入框**同区域互斥**（同一列同位——显隐由 input.js 切内联 display）；
//           不参与对话流、不进会话存档。
//
// 推法：后端有变化才推（无变化零字节）；推即整段，前端只管替换。
//
// 语义（A196 · 契约 §12.5）：type ∈ {thinksse, replysse, toolrun, empty}
//   thinksse / replysse——context = 该流当前全文（文本，直接投给对应渲染件）
//   toolrun——context = 未完成工具卡数组 JSON（逐卡复用持久区工具卡渲染件）
//   empty——链路正常、内容为空：不产元素（面板无内容即零高，不占位）
// ═══════════════════════════════════════════

/// 临时区容器——输入区切换面板
function liveContainer() {
    return document.getElementById('chatLivePanel');
}

/// 临时段入口——整体替换（状态投影语义）；**功能隔离**：单段异常不中断其他
function liveApply(seg) {
    var box = liveContainer();
    if (!box) {
        return;
    }
    box.textContent = '';
    var type = seg ? (seg.type || 'empty') : 'empty';
    if (type === 'empty') {
        return;
    }
    var ctx = seg ? ((seg.context === undefined || seg.context === null) ? '' : String(seg.context)) : '';
    try {
        liveRender(type, ctx, box);
    } catch (e) {
        box.appendChild(buildRenderErrorBlock({ type: type, payload: { text: ctx } }, e));
    }
}

/// 渲染分发——按 type 把 context 投给对应结构（toolrun 为数组：逐卡复用持久区工具卡渲染件）
function liveRender(type, ctx, box) {
    if (type === 'toolrun') {
        // 后端保证合法 JSON 数组（空数组 = 无在途工具，阵列零卡）；解析异常由 liveApply 兜底
        var cards = JSON.parse(ctx.length > 0 ? ctx : '[]');
        for (var i = 0; i < cards.length; i++) {
            box.appendChild(buildToolBlock(cards[i], null, 'toolrun'));
        }
        return;
    }
    var fn = LIVE_RENDERERS[type];
    if (typeof fn !== 'function') {
        box.appendChild(buildUnknownBlock({ type: type, payload: { text: ctx } }));
        return;
    }
    box.appendChild(fn({ text: ctx }));
}
