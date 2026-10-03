// ═══════════════════════════════════════════
// chat/live.js —— 临时区（契约 §12.2 ③ · §6.1-F/G）
//
// 定位：临时区的唯一应用入口。**全量镜像**——每次收帧整体替换面板内容，不比对、不 diff。
//
// 物理位置：输入区上方的切换面板（与对话区物理分离——契约 E：同容器内不存在两类条目共存，
//           故无区分、无配对、无寻址）。不参与对话流、不进会话存档。
//
// 推法：后端有变化才推（无变化零字节）；推即全量，前端只管替换。
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

/// 单条渲染——type 直指渲染函数；未登记 type → 兜底报错气泡（lib/fallback.js，与持久区共用）
function liveRender(item) {
    if (!item) {
        return null;
    }
    var fn = LIVE_RENDERERS[item.type];
    if (typeof fn !== 'function') {
        return buildUnknownBlock(item);
    }
    return fn(item.payload || {});
}
