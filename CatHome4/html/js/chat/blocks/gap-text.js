// ═══════════════════════════════════════════
// blocks/gap-text.js —— gap_text 块（persist 类）
//
// 契约：
//   item = { type:'gap_text', ts, msgIndex, round, payload:{ text } }
//   text = Markdown 原文（工具轮 seal 文本——模型调用工具前说的话）
//
// 产出：.chat-row.assistant > .chat-bubble > (图片组?) + .md-block[.md-copy]
//
// A188：间隙文本自 `text` 分型独立——消费方（QQ 转发 / 会话存档 / 前端）据此区分
//       「正式回复」与「工具轮间隙」两义；外观形态与 `text` 同（气泡 + MD 填充）。
//       填充逻辑复用 `fillMdBlock`（blocks/text.js——按 chat.html 清单顺序，本件在其后加载）。
// ═══════════════════════════════════════════

function buildGapTextBlock(payload) {
    // payload.text = Markdown 原文
    var p = payload || {};
    var row = blockRow('gap_text');
    var bubble = el('div', bodyClass('gap_text'));
    row.appendChild(bubble);
    fillMdBlock(bubble, p.text || '');
    return row;
}
