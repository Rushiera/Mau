// ═══════════════════════════════════════════
// chat/fx/pending.js —— 插话队列显示（独立功能件 A173）
// 启动：无需启动（纯函数入口——入列 / 出列 / 清队由主干与输入出口调用）
//
// 语义：**本地发送记录（纯展示）**——入列在发送时刻（input.js），出列在内核 user 块到达时刻
//      （persist.js 的 append 逐条钩子）；全量重绘（首连 / 重连 / 新会话）即清队——队列只覆盖「在途」。
// 契约依据：user 块 = 内核确认消息进前文的唯一出口（单向数据流）——「本地已发、内核未确认」的窗口
//          就是队列的展示窗口；新契约下该判据与旧件同构（旧件 shift 于 chatOnUser，新件 consume 于 append）。
// 形态：#chatPendingPanel 内逐条「⏳ 文本」（chat.css 既有规则；空表即 `:empty` 隐藏）
// ═══════════════════════════════════════════

/// 队列条目（发送顺序；纯展示）
var pendQueue = [];

/// 入列——发送时刻调用（文本为空但有图时记「（图片）」）
function pendingAdd(text) {
    pendQueue.push(text);
    pendingRender();
}

/// 出列——内核 user 块到达（FIFO 队首移除）
function pendingConsume() {
    if (pendQueue.length === 0) {
        return;
    }
    pendQueue.shift();
    pendingRender();
}

/// 清队——全量重绘 / 投递未受理（本地在途记录失去意义）
function pendingClear() {
    if (pendQueue.length === 0) {
        return;
    }
    pendQueue = [];
    pendingRender();
}

/// 渲染——空表清空面板（`:empty` 规则自动隐藏）
function pendingRender() {
    var panel = document.getElementById('chatPendingPanel');
    if (!panel) {
        return;
    }
    if (pendQueue.length === 0) {
        panel.textContent = '';
        return;
    }
    var html = '';
    for (var i = 0; i < pendQueue.length; i = i + 1) {
        html = html + '<div>⏳ ' + mdEscapeHtml(pendQueue[i]) + '</div>';
    }
    panel.innerHTML = html;
}
