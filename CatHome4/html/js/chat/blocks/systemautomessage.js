// ═══════════════════════════════════════════
// blocks/systemautomessage.js —— 系统注入块（persist 类）
//
// 契约：
//   item = { type:'systemautomessage', ts, msgIndex, round, payload:{ text, source?, origin? } }
//   source = 来源族名（systemauto / sleep / timer / delay / restart）
//   text   = 落前文文本（含固定行头前缀 [systemauto]）
//   渲染顺序即到达顺序——本层不排序、不去重、不算键
//
// 产出：.chat-row.sysmsg > .chat-bubble（挂载由调用方决定）
//
// 格式：暂用 user 同款蓝色气泡、靠左（后续单开美化）
// 来源：后端 ChatSession.AppendInjection（sys 路径）→ SessionViewStore.OnSystemMessage
//
// 丢弃（属主干，不入素材）：
//   · DOM 挂载（chatMsgs.appendChild）与滚动跟随（chatScrollSoon）
//   · 来源族名文案化——各 payload.text 自带渠道前缀，前端不另拼
// ═══════════════════════════════════════════

function buildSystemAutoMessageBlock(payload) {
    // payload.text 为最终展示文本（前缀已由后端落前文时套好）；source = 来源族名（暂不参与渲染）
    var p = payload || {};
    var row = blockRow('systemautomessage');
    var bubble = el('div', bodyClass('systemautomessage'));
    row.appendChild(bubble);
    bubble.textContent = p.text || '';
    return row;
}
