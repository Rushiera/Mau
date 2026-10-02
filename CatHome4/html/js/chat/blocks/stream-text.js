// ═══════════════════════════════════════════
// blocks/stream-text.js —— stream.text（live 类 · 回复流式）
//
// 契约：
//   item = { type:'stream.text', payload:{ kind:'text', text } }
//   text = 当前该流式块的全部文本（live 区按帧全量镜像）；空文本 = 尚无内容，不建块
//
// 用法（两态）：
//   var h = buildStreamText();              // 造块（新块到达时一次）
//   appendStreamText(h, chunk);             // 追加增量（每帧）
//   setStreamText(h, fullText);             // 或按全量镜像覆盖
//
// 产出：.chat-row.assistant > .chat-bubble[.streaming] >（专用文本节点）
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
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', 'chat-bubble streaming');
    row.appendChild(bubble);
    return { row: row, bubble: bubble };
}

function appendStreamText(h, chunk) {
    // 增量追加——专用文本节点（textContent 拼接会销毁子元素：工具卡 / details 被后续 text 事件清空）
    if (!h || !h.bubble) { return; }
    var text = (chunk === undefined || chunk === null) ? '' : String(chunk);
    if (text.length === 0) { return; }
    var bubble = h.bubble;
    if (!bubble.textNode) {
        bubble.textNode = document.createTextNode('');
        bubble.insertBefore(bubble.textNode, bubble.firstChild);
    }
    bubble.textNode.data = bubble.textNode.data + text;
}

function setStreamText(h, fullText) {
    // 全量镜像覆盖——live 区按帧全量送达时用此入口（与增量模式二选一，由主干的传输模式决定）
    if (!h || !h.bubble) { return; }
    var text = (fullText === undefined || fullText === null) ? '' : String(fullText);
    var bubble = h.bubble;
    if (!bubble.textNode) {
        bubble.textNode = document.createTextNode('');
        bubble.insertBefore(bubble.textNode, bubble.firstChild);
    }
    bubble.textNode.data = text;
}

function sealStreamText(h) {
    // 收尾——撤进行中外观（流式块完成时调用；块的去留由主干按 live 镜像决定）
    if (!h || !h.bubble) { return; }
    h.bubble.classList.remove('streaming');
}
