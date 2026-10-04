// ═══════════════════════════════════════════
// chat/live.js —— 临时区（契约 §12.2 ③ · §6.1-F/G）
//
// 定位：临时区的唯一应用入口。**全量镜像**——每次收帧整体替换面板内容，不比对、不 diff。
//
// 物理位置：输入区内、与输入框**同区域互斥**（同一列同位——显隐由 input.js 切内联 display）；
//           不参与对话流、不进会话存档。
//
// 推法：后端有变化才推（无变化零字节）；推即全量，前端只管替换。
//
// 条目语义（A187 · 契约 §12.5「live 生命周期语义」）：type ∈ {thinksse, replysse, toolrun}——运行态镜像，
//   前端按 type 渲染、不判终态；条目自段内移出即不渲染。全空时后端推单条 `empty`（链路正常、内容为空）
//   → 本层不产元素 → 面板无内容即零高（不占位）。
// ═══════════════════════════════════════════

/// 临时区容器——输入区切换面板
function liveContainer() {
    return document.getElementById('chatLivePanel');
}

/// 临时段入口——整体替换（全量镜像语义，不做增量合并）；**功能隔离**：单条异常不中断整批
function liveApply(seg) {
    var box = liveContainer();
    if (!box) {
        return;
    }
    box.textContent = '';
    var items = seg ? (seg.items || []) : [];
    for (var i = 0; i < items.length; i++) {
        var node = null;
        try {
            node = liveRender(items[i]);
        } catch (e) {
            node = buildRenderErrorBlock(items[i], e);
        }
        if (node) {
            box.appendChild(node);
        }
    }
}

/// 单条渲染——type 直指渲染函数；`empty` = 全空占位（链路正常、内容为空）不产元素（契约 §12.5）；
/// 未登记 type → 兜底报错气泡（lib/fallback.js，与持久区共用）
function liveRender(item) {
    if (!item) {
        return null;
    }
    if (item.type === 'empty') {
        return null;
    }
    var fn = LIVE_RENDERERS[item.type];
    if (typeof fn !== 'function') {
        return buildUnknownBlock(item);
    }
    return fn(item.payload || {});
}
