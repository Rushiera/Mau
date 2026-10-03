// ═══════════════════════════════════════════
// blocks/text.js —— text 块（persist 类）
//
// 契约：
//   item = { type:'text', ts, msgIndex, round, payload:{ text } }
//   text = Markdown 原文（含图片包裹时先出缩略图组）
//
// 产出：.chat-row.assistant > .chat-bubble > (图片组?) + .md-block[.md-copy]
//
// 来源：chat-view.js chatMdFill（第 714-726 行）+ chatAppendHistoryBlock「text」分支
//
// 丢弃（属主干，不入素材）：
//   · DOM 挂载 / 滚动跟随
//   · 节点操作条（chatAppendNodeActions，第 389-426 行）—— 内嵌 fetch('session.rollback'/'session.fork')
//     与 chatInfo 直写；回滚/分支属主干指令面，素材层不承载网络语义
//   · 历史收尾（chatHistoryFinish / chatRenderHistory）—— 属主编排
//   · 顶部信息行（前文 N 条 / tokens）—— 属主干状态面
// ═══════════════════════════════════════════

function buildTextBlock(payload) {
    // payload.text = Markdown 原文
    var p = payload || {};
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', formClass('text'));
    row.appendChild(bubble);
    fillMdBlock(bubble, p.text || '');
    return row;
}

function fillMdBlock(bubble, content) {
    // assistant 泡内容填充——无图片包裹走原路径（md-block 单块）；有包裹 = 缩略图组 + MD 正文块
    var r = imgSplit(content);
    var text = (r.items.length > 0) ? r.body : content;
    if (r.items.length > 0) {
        bubble.appendChild(imageGroup(r.items));
    }
    var md = el('div', 'md-block');
    md.innerHTML = mdToHtml(text);
    mdBindCopy(md);   // 代码块 / 表格挂复制按钮（整块渲染后一次性挂载）
    bubble.appendChild(md);
}
