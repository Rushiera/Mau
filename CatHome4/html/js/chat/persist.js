// ═══════════════════════════════════════════
// chat/persist.js —— 持久对话区（契约 §12.2 ② · §12.3）
//
// 定位：持久区的唯一应用入口。**无键、无配对、无排序**——按到达顺序挂载（推送序即渲染序）。
//
// 两种模式（帧内 persist.mode）：
//   full   —— 清区整体重绘（连接首帧 / 重连 / 新会话重置后重发）
//   append —— 逐条追加（新持久条目）
//
// 边界：块函数只产出元素（素材零全局状态）；挂载、滚动跟随归本层。
//       本层不判流式结束、不判换手、不配对——契约 D「前端 = 纯渲染」。
// ═══════════════════════════════════════════

/// 持久区容器——对话区
function persistContainer() {
    return document.getElementById('chatMsgs');
}

/// 持久段入口——按帧内 mode 分派（full 先清后绘 / append 逐条追加）
function persistApply(seg) {
    if (!seg) {
        return;
    }
    var items = seg.items || [];
    if (seg.mode === 'full') {
        persistFull(items);
        return;
    }
    persistAppend(items);
}

/// 全量重绘——清空后按序重建，并强制滚底（内容整体换过，跟随复位）
function persistFull(items) {
    var box = persistContainer();
    if (!box) {
        return;
    }
    box.textContent = '';
    // 插话队列——全量重绘即清队（连接首帧 / 重连 / 新会话：本地在途记录失去意义）
    if (typeof pendingClear === 'function') {
        pendingClear();
    }
    persistAppend(items);
    scrollBottomNow(true);
}

/// 追加——逐条渲染挂载；**功能隔离**：单块渲染异常不中断整批（异常块落可见错误气泡）
function persistAppend(items) {
    var box = persistContainer();
    if (!box || !items || items.length === 0) {
        return;
    }
    for (var i = 0; i < items.length; i++) {
        // 插话队列——user 块到达 = 内核确认，本地在途记录 FIFO 出队
        if (items[i] && items[i].type === 'user' && typeof pendingConsume === 'function') {
            pendingConsume();
        }
        var node = null;
        try {
            node = persistRender(items[i]);
        } catch (e) {
            node = buildRenderErrorBlock(items[i], e);
        }
        if (node) {
            box.appendChild(node);
        }
    }
    scrollSoon();
}

/// 单条渲染——type 直指渲染函数；未登记 type → **兜底报错气泡**（失败可见：块不消失，显式告诉人不认识）
function persistRender(item) {
    if (!item) {
        return null;
    }
    var fn = PERSIST_RENDERERS[item.type];
    if (typeof fn !== 'function') {
        return buildUnknownBlock(item);
    }
    return fn(item.payload || {});
}

/// 未识别块型兜底——默认报错气泡（type + 载荷摘要；两区共用：持久区与临时区同一兜底件）
function buildUnknownBlock(item) {
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', 'chat-bubble error');
    var payload = '';
    try {
        payload = JSON.stringify(item.payload || {});
    } catch (e) {
        payload = '[载荷不可序列化]';
    }
    if (payload.length > 200) {
        payload = payload.substring(0, 200) + '…';
    }
    bubble.textContent = '⚠ 未识别的块型「' + item.type + '」——前端渲染表无此 type；载荷：' + payload;
    row.appendChild(bubble);
    return row;
}

/// 渲染异常兜底——单块抛错时替代该块（**功能隔离**：一个异常不中断其他；异常可见，不静默丢块）
function buildRenderErrorBlock(item, err) {
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', 'chat-bubble error');
    var msg = (err && err.message) ? err.message : String(err);
    bubble.textContent = '⚠ 块渲染失败「' + ((item && item.type) ? item.type : '?') + '」：' + msg + '——该块已跳过，其余不受影响';
    row.appendChild(bubble);
    return row;
}
