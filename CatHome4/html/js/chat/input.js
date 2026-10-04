// ═══════════════════════════════════════════
// chat/input.js —— 输入区与动作按钮
//
// 定位：用户侧唯一出口。发送 / 停止 / 继续 / 新会话 / 刷新 / 临时区切换。
// 指令一律走指令总线（POST /api/v1/command）——前端不拼业务指令串以外的逻辑。
//
// 输入区与临时区**同区域互斥**：同一列同位，输入框与 live 面板二选一（代码切内联 display）；
//   回车发送时自动切回输入态，想看临时内容自己点回来（纯前端可视化，判据取全局态）。
//   本件只切本区两块——不碰对话区（#chatMsgs）与滚动带（#chatScrollBand，显隐归 fx/scroll）。
// ═══════════════════════════════════════════

/// 临时区显示态——false = 输入态（输入框可见），true = 临时内容（面板可见）
var liveShown = false;

/// 指令投递——统一出口（失败可见；投递即回执，结果异步经 SSE 回来）
/// images 可选——图片绝对路径列表（待发区）；编号与包裹由后端组装，前端只投路径
function postCommand(text, images) {
    var payload = { text: text };
    if (images && images.length > 0) {
        payload.images = images;
    }
    return fetch('/api/v1/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    }).then(function (r) {
        return r.json();
    }).then(function (j) {
        if (!j || j.ok !== true) {
            warn('指令未受理：' + text + (j && j.error ? '（' + j.error + '）' : ''));
        }
        return j;
    }).catch(function (e) {
        warn('指令投递失败', e);
        return null;
    });
}

/// 发送——空文本且无待发图零动作；发送后自动切回输入态（用户想看内容而非留在临时面板）
function chatSend() {
    var box = document.getElementById('chatSendInput');
    if (!box) {
        return;
    }
    var text = (box.value || '').replace(/^\s+|\s+$/g, '');
    if (typeof pendBusy === 'function' && pendBusy()) {
        // 上传未完成不发——防漏图（提示走待发区尾部，不抢顶部状态位）
        pendNotice('图片上传中——稍候再发');
        return;
    }
    var paths = (typeof pendPaths === 'function') ? pendPaths() : [];
    if (text.length === 0 && paths.length === 0) {
        return;
    }
    // 插话队列——本地入列（内核 user 块到达时出列——见 pending.js / persist.js 钩子）
    if (typeof pendingAdd === 'function') {
        pendingAdd(text.length > 0 ? text : '（图片）');
    }
    // 🔴 用户消息必须带 `Chat ` 前缀——宿主 `DispatchCommandForCat` 只认前缀行，裸文本 `return false` 不投递
    postCommand('Chat ' + text, paths).then(function (j) {
        if (!j || j.ok !== true) {
            // 投递未受理——本地在途记录失去意义（防幽灵队列）
            if (typeof pendingClear === 'function') {
                pendingClear();
            }
        }
    });
    box.value = '';
    if (typeof pendClear === 'function') {
        pendClear();
    }
    showStreamArea();
}

/// 停止本轮——已生成内容保留（后端负责前文格式修复）
function chatPause() {
    postCommand('cat.pause');
}

/// 继续——用当前前文直接再发一次请求（不追加消息）
function chatContinue() {
    postCommand('cat.continue');
}

/// 新会话——清前文并重新注入；服务端随后在流内重发全量帧
function chatNewSession() {
    postCommand('session.new');
}

/// 刷新——纯前端重建：断开重连即由服务端重推全量帧（零本地状态修补）
function chatRefresh() {
    if (chatSse) {
        chatSse.close();
        chatSse = null;
    }
    chatConnect();
}

/// 临时区 / 输入区切换——同区域互斥（按钮点击入口）
function chatLiveToggle() {
    liveShown = (liveShown !== true);
    inputSwap(liveShown);
}

/// 区域显隐单点——输入框与 live 面板二选一（元素缺失即跳过）；按钮激活态随动
/// 面板高度上限 = 输入框实测高度（同一把尺：切换前后区域高度一致，不被撑大）
function inputSwap(shown) {
    var input = document.getElementById('chatSendInput');
    var panel = liveContainer();
    if (panel) {
        if (shown === true && input) {
            var h = input.offsetHeight;
            panel.style.maxHeight = (h > 0 ? h : 0) + 'px';
        }
        panel.style.display = shown ? '' : 'none';
    }
    if (input) {
        input.style.display = shown ? 'none' : '';
    }
    var btn = document.getElementById('chatLiveToggle');
    if (btn) {
        if (shown) {
            btn.classList.add('on');
        } else {
            btn.classList.remove('on');
        }
    }
}

/// 切回输入态——发送时的自动动作（已在输入态则零动作）
function showStreamArea() {
    if (liveShown === true) {
        liveShown = false;
        inputSwap(false);
    }
    scrollBottomNow(true);
}

/// 输入区接线——元素缺失即跳过（防御式）；本件加载于页面尾部，DOM 已就绪
function inputBind() {
    var send = document.getElementById('chatSendBtn');
    if (send) {
        send.addEventListener('click', chatSend);
    }
    var input = document.getElementById('chatSendInput');
    if (input) {
        input.addEventListener('keydown', function (e) {
            // Enter 发送（阻止 textarea 插入换行）；Shift+Enter 走默认换行
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                chatSend();
            }
        });
    }
    var pause = document.getElementById('chatPause');
    if (pause) {
        pause.addEventListener('click', chatPause);
    }
    var cont = document.getElementById('chatContinue');
    if (cont) {
        cont.addEventListener('click', chatContinue);
    }
    var fresh = document.getElementById('chatNew');
    if (fresh) {
        fresh.addEventListener('click', chatNewSession);
    }
    var reload = document.getElementById('chatRefresh');
    if (reload) {
        reload.addEventListener('click', chatRefresh);
    }
    var toggle = document.getElementById('chatLiveToggle');
    if (toggle) {
        toggle.addEventListener('click', chatLiveToggle);
    }
    var jump = document.getElementById('chatJumpBottom');
    if (jump) {
        jump.addEventListener('click', jumpBottom);
    }
}

inputBind();
