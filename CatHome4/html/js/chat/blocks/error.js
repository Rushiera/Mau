// ═══════════════════════════════════════════
// blocks/error.js —— error 块（persist 类 · LLM 错误）
//
// 契约：
//   item = { type:'error', ts, msgIndex, round, payload:{ text } }
//   text = 错误原文（缺省「LLM 错误」）
//
// 产出：.chat-row.assistant > .chat-bubble.error
//
// 来源：chat-core.js chatRenderError（第 507-513 行）+ chat-view.js chatAppendHistoryBlock「error」分支
//
// 取舍：错误与重试解耦——错误块只表达错误本身，重试过程归 blocks/retry.js（莎 2026-09-16 拍板 A）
//       丢弃：到达即恢复 idle 的状态迁移（属主干）、流式容器 seal（属主干）
// ═══════════════════════════════════════════

function buildErrorBlock(payload) {
    // 错误气泡——恒定新建独立块（不与重试气泡合并）
    var p = payload || {};
    var row = el('div', 'chat-row assistant');
    var bubble = el('div', 'chat-bubble error');
    bubble.textContent = p.text || 'LLM 错误';
    row.appendChild(bubble);
    return row;
}
