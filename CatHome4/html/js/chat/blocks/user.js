// ═══════════════════════════════════════════
// blocks/user.js —— user 块（persist 类）
//
// 契约：
//   item = { type:'user', ts, msgIndex, round, payload:{ text, src? } }
//   src 缺字段 / 'user' = 人工输入（靠右）；非空且非 'user' = 系统注入（行变体 `.sys`——靠左，A204）
//   渲染顺序即到达顺序——本层不排序、不去重、不算键
//
// 产出：.chat-row.user[.sys] > .chat-bubble（挂载由调用方决定）
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
    // payload.text 为最终展示文本（前缀已由主干拼好）；payload.src = 来源标记（A204）
    var p = payload || {};
    var row = blockRow('user');
    var src = rowSourceClass(p);
    if (src) {
        // 来源变体——系统注入行与常规 user 左右翻转（靠左；类名归 registry 声明）
        row.classList.add(src);
    }
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
