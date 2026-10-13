// ═══════════════════════════════════════════
// blocks/user.js —— user 块（persist 类 · 人工路径专属）
//
// 契约：
//   item = { type:'user', ts, msgIndex, round, payload:{ text, origin? } }
//   src 字段已退役（A219）——系统注入改独立块 systemautomessage；本块恒为人工输入（靠右）
//   渲染顺序即到达顺序——本层不排序、不去重、不算键
//
// 产出：.chat-row.user > .chat-bubble（挂载由调用方决定）
//
// 来源：后端 ChatSession.AppendInjection（人工路径）→ SessionViewStore.OnUserMessage
//
// 丢弃（属主干，不入素材）：
//   · DOM 挂载（chatMsgs.appendChild）与滚动跟随（chatScrollSoon）
//   · 插话队列确认（chatPending.shift / chatRenderPending）—— 属主干交互态
// ═══════════════════════════════════════════

function buildUserBlock(payload) {
    // payload.text 为最终展示文本；payload.origin = QQ 注入轮起点锚（本层不消费）
    var p = payload || {};
    var row = blockRow('user');
    var bubble = el('div', bodyClass('user'));
    row.appendChild(bubble);
    fillUserBubble(bubble, p.text || '');
    return row;
}

function fillUserBubble(bubble, content) {
    // user 泡内容填充——无图片包裹走原路径（textContent，零回归）；有包裹 = 前段 + 缩略图组 + 后段
    // （2026-10-06 放宽：包裹可位于任意位置；组装侧仍拼在正文之前——用户消息形态不变）
    var r = imgSplit(content);
    if (r.items.length === 0) {
        bubble.textContent = content;
        return;
    }
    if (r.before.length > 0) {
        bubble.appendChild(elText('div', 'chat-user-text', r.before));
    }
    bubble.appendChild(imageGroup(r.items));
    if (r.after.length > 0) {
        bubble.appendChild(elText('div', 'chat-user-text', r.after));
    }
}
