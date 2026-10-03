// ═══════════════════════════════════════════
// blocks/stream-text.js —— stream.text（live 类 · 回复流式）
//
// 契约：
//   item = { type:'stream.text', payload:{ kind:'text', text } }
//   text = 当前该流式块的全部文本（live 区按帧全量镜像）；空文本 = 尚无内容，不建块
//
// 用法：
//   var h = buildStreamText();              // 造块（新块到达时一次）
//   setStreamText(h, fullText);             // 按帧全量镜像覆盖（live 区按帧全量送达）
//
// 产出：.chat-row.assistant > .chat-plain[.streaming] >（专用文本节点）（形态见 registry.js §形态声明）
//
// 来源：chat-core.js chatOnLiveStream 文本分支（第 440-450 行）+ chat-view.js chatAppend（第 14-24 行）
//
// 丢弃（属主干，不入素材）：
//   · 容器键表（viewContainers —— 新契约前端零键算术；句柄由主干持有）
//   · 运行态类标记（chatSyncRunningMarks —— 后端态驱动，属主干）
//   · 滚动跟随（chatScrollSoon）、保活（chatKeepAlive）、空增量判定
// ═══════════════════════════════════════════

function buildStreamText() {
    // 流式文本块——新建（气泡内只有文本节点，随增量增长）
    var row = blockRow('stream.text');
    var bubble = el('div', bodyClass('stream.text'));
    row.appendChild(bubble);
    return { row: row, bubble: bubble };
}

function setStreamText(h, fullText) {
    // 全量镜像覆盖——live 区按帧全量送达时用此入口（无增量模式：live 区按帧全量镜像）
    if (!h || !h.bubble) { return; }
    var text = (fullText === undefined || fullText === null) ? '' : String(fullText);
    var bubble = h.bubble;
    if (!bubble.textNode) {
        bubble.textNode = document.createTextNode('');
        bubble.insertBefore(bubble.textNode, bubble.firstChild);
    }
    bubble.textNode.data = text;
}

/// live 区入口——按帧全量镜像，一次到位写入全文（registry.js 的 LIVE_RENDERERS 消费）
function liveStreamText(payload) {
    var h = buildStreamText();
    setStreamText(h, (payload || {}).text || '');
    return h.row;
}
