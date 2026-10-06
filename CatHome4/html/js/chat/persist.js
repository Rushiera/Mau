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
//       兜底件（未识别 type / 渲染异常）→ `lib/fallback.js`（两区共用，A178 归位）
// ═══════════════════════════════════════════

// ── type 归一（前端视觉口径 · 2026-10-05）─────────────────────
// `gap_text`（工具轮间隙文本）与 `text`（正式回复）在前端**视觉同形**——同气泡形态 / 同行语义 /
// 同 MD 填充件；语义区分（QQ `/last` 只取正式回复 · 会话存档不进间隙文本）由**后端消费方**承担。
// 前端零业务效果 → 收包即归一，不设第二件、不设第二张分型表。
// 新增同形 type：本表登记一行即可。
var TYPE_ALIAS = {
    'gap_text': 'text'
};

/// 类型归一——收包后第一个动作（`persistAppend` 内逐条调用；full / append 两路同经此）
function normalizeType(t) {
    return TYPE_ALIAS[t] || t;
}

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
    scrollBottomNow();
}

/// 追加——逐条渲染挂载；**功能隔离**：单块渲染异常不中断整批（异常块落可见错误气泡）
function persistAppend(items) {
    var box = persistContainer();
    if (!box || !items || items.length === 0) {
        return;
    }
    // 跟随判定前置——**追加前**采样视口位置（追加后采样会被新块自身高度顶出阈值；2026-10-06 判例）
    var follow = scrollWanted();
    for (var i = 0; i < items.length; i++) {
        // type 归一——收包后第一个动作：此后所有 type 消费（判据 / 分派 / 兜底）都在归一口径上
        if (items[i]) {
            items[i].type = normalizeType(items[i].type);
        }
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
    if (follow) {
        scrollFollow();
    }
}

/// 单条渲染——type 直指渲染函数；未登记 type → 兜底报错气泡（lib/fallback.js，两区共用）
function persistRender(item) {
    if (!item) {
        return null;
    }
    var fn = PERSIST_RENDERERS[item.type];
    if (typeof fn !== 'function') {
        return buildUnknownBlock(item);
    }
    // 第二参 = 整条条目——块级字段取用口（如 text 块操作条读 msgIndex 定回滚 / 分支切点）
    return fn(item.payload || {}, item);
}
