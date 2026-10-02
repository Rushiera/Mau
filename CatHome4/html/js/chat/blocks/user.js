// ═══════════════════════════════════════════
// blocks/user.js —— user 块（persist 类）
//
// 契约：
//   item = { type:'user', ts, msgIndex, round, payload:{ text } }
//   渲染顺序即到达顺序——本层不排序、不去重、不算键
//
// 产出：.chat-row.user > .chat-bubble[.system]（挂载由调用方决定）
//
// 来源：chat-view.js chatAppendHistoryBlock「user」分支 + chatUserFill（第 630-644 行）
//
// 丢弃（属主干，不入素材）：
//   · DOM 挂载（chatMsgs.appendChild）与滚动跟随（chatScrollSoon）
//   · 来源前缀拼接（[SystemAuto] / ⏰ 定时 / 💤 唤醒 / ⏳ 定时注入 / ⚙️ 系统 / 🔄 回执）
//     —— 属主干语义：主干在调用前把前缀拼进 content 或另行传 source
//   · 插话队列确认（chatPending.shift / chatRenderPending）—— 属主干交互态
// ═══════════════════════════════════════════

function buildUserBlock(payload) {
    // payload.text 为最终展示文本（前缀已由主干拼好）；旧落盘块字段为 content——兼容读，旧数据退役后移除
    var p = payload || {};
    var row = el('div', 'chat-row user');
    var bubble = el('div', formClass('user'));
    row.appendChild(bubble);
    fillUserBubble(bubble, p.text || p.content || '');
    return row;
}

function fillUserBubble(bubble, content) {
    // user 泡内容填充——无图片包裹走原路径（textContent，零回归）；有包裹 = 缩略图组 + 正文文本
    var r = imgSplit(content);
    if (r.items.length === 0) {
        bubble.textContent = content;
        return;
    }
    bubble.appendChild(imageGroup(r.items));
    if (r.body.length > 0) {
        bubble.appendChild(elText('div', 'chat-user-text', r.body));
    }
}
