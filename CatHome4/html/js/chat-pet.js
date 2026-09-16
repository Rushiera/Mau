// CH4 外观层——chat-pet.js：桌宠渲染（纯前端；对话页的组成部分）
// 定位：读后端权威运行态（chatRunState——六态）→ 态映射动画资源 → 调度空闲行为与交互反应
// 数据源：chat-core.js 的 chatRunState（经 chatRenderStatus 汇聚点回调）；SSE 断线由引导层 onerror/onopen 置位
// 铁律：前端零业务逻辑——本模块只做「态 / 交互 → 资源」映射与动画调度，不产生业务事件、不介入会话状态机
// 资源：html/pet/*.webp（286x256 原尺寸 1:1 显示 · 20ms/帧 · 透明底 · 圆角矩形边缘羽化；源素材 Workspace/pet/*.gif）
//
// 形态语义（素材约定）：
//   loop-*  循环态（link/wait/think/tool/run/reply/sseErr/idle1-4/idle-hook/idle-sleep）
//   before-* 前置过渡（播一遍）——before-tool 进工具前 / before-idle 一轮收尾回空闲前 / before-idle-hook 移入 / before-idle-sleep 入睡
//   after-*  后置反应（播一遍）——after-idle-check1-5 点击后的摸头反应，随机五选一
// 空闲节律（莎 2026-09-16 定）：进空闲随机一张基础待机播 8s → 直接进入睡；hook/check 结束后同样回到这个 8s 周期
// 渐隐规则（莎 2026-09-16 定）：before → loop、loop → after 两段是连续动作——**瞬切**；其余切换走 0.25s 交叉溶解

// [段1] 状态与常量
var chatPetImgs = [null, null];   // 双层图片元素（交叉溶解——交替作为当前层）
var chatPetLayer = 0;             // 当前显示层下标
var chatPetCur = '';              // 当前资源名（不含扩展名）——同值不切换（防重复溶解）
var chatPetDrag = null;           // 拖动状态 {dx, dy, x0, y0}——null = 未按下
var chatPetDragMoved = false;     // 本次按下是否发生位移（决定随后 click 是否算摸头）
var chatPetOffline = false;       // SSE 断线标记（引导层 onerror/onopen 置位）
var chatPetMode = '';             // 调度模式：run（活跃态）/ idle（空闲）/ offline
var chatPetKey = '';              // 当前态 key（六态名 / idle / offline）
var chatPetKeyAt = 0;             // 进入当前态的时间戳——桌宠自算停留时长（前端调度口径，非展示口径）
var chatPetTimer = null;          // 显示调度定时器（单一定时器——任何切换先清）
var chatPetIdleMode = '';         // 空闲子模式：intro / base / hook / check / sleep
var chatPetIdleIdx = -1;          // 上次基础待机下标（避免连续重复）
var chatPetCheckIdx = -1;         // 上次摸头反应下标（避免连续重复）
var chatPetHovered = false;       // 鼠标是否停留在桌宠上（决定反应结束后回哪个子模式）
var chatPetLastInstant = false;   // 上一次换图是否为瞬切（before→loop / loop→after）——调度与诊断用

// 六态 → 循环资源
var chatPetPhaseMap = { link: 'loop-link', wait: 'loop-wait', think: 'loop-think', tool: 'loop-tool', run: 'loop-run', reply: 'loop-reply' };

var CHAT_PET_IDLE_BASE = ['loop-idle1', 'loop-idle2', 'loop-idle3', 'loop-idle4'];   // 基础待机四选一（随机）
var CHAT_PET_CHECKS = [
    { name: 'after-idle-check1', ms: 820 },
    { name: 'after-idle-check2', ms: 820 },
    { name: 'after-idle-check3', ms: 820 },
    { name: 'after-idle-check4', ms: 820 },
    { name: 'after-idle-check5', ms: 580 }
];
var CHAT_PET_IDLE_BASE_HOLD_MS = 8000;   // 基础待机时长——8s 到点**直接进入睡**（不是换下一张）
var CHAT_PET_BEFORE_TOOL_MS = 2640;      // before-tool 时长
var CHAT_PET_BEFORE_IDLE_MS = 1980;      // before-idle 时长
var CHAT_PET_BEFORE_HOOK_MS = 1140;      // before-idle-hook 时长
var CHAT_PET_BEFORE_SLEEP_MS = 1980;     // before-idle-sleep 时长

// 全量资源名（预加载清单——须与 html/pet/ 目录一致）
var chatPetFiles = [
    'loop-link', 'loop-wait', 'loop-think', 'loop-tool', 'loop-run', 'loop-reply', 'loop-sseErr',
    'before-tool', 'before-idle',
    'loop-idle1', 'loop-idle2', 'loop-idle3', 'loop-idle4',
    'loop-idle-hook', 'before-idle-hook',
    'loop-idle-sleep', 'before-idle-sleep',
    'after-idle-check1', 'after-idle-check2', 'after-idle-check3', 'after-idle-check4', 'after-idle-check5'
];

// [段2] 基础操作
function chatPetClearTimer()
{
    // 显示调度单一定时器纪律——任何切换先清（防陈旧定时器改图）
    if (chatPetTimer !== null)
    {
        clearTimeout(chatPetTimer);
        chatPetTimer = null;
    }
}

function chatPetPick(n, last)
{
    // 随机取下标——避免与上一次相同（n=1 时直接返回 0）
    if (n <= 1) { return 0; }
    var idx = last;
    var guard = 0;
    while (idx === last && guard < 20)
    {
        idx = Math.floor(Math.random() * n);
        guard = guard + 1;
    }
    return idx;
}

function chatPetShow(name, instant)
{
    // 换图——instant=true 时瞬切（before→loop / loop→after 的连续动作），否则双层交叉溶解 0.25s
    if (chatPetImgs[0] === null) { return; }
    if (chatPetCur === name) { return; }
    chatPetCur = name;
    chatPetLastInstant = (instant === true);
    var cur = chatPetImgs[chatPetLayer];
    var next = chatPetImgs[1 - chatPetLayer];
    if (instant === true)
    {
        next.style.transition = 'none';
        cur.style.transition = 'none';
    }
    next.src = 'pet/' + name + '.webp';
    next.style.opacity = '1';
    cur.style.opacity = '0';
    if (instant === true)
    {
        void next.offsetWidth;          // 强制应用后再恢复过渡设置（此后切换仍溶解）
        next.style.transition = '';
        cur.style.transition = '';
    }
    chatPetLayer = 1 - chatPetLayer;
}

// [段3] 运行态与断线
function chatPetEnterRun(key)
{
    // 活跃态——工具态前置：从别的态进 tool 时先播 before-tool，再瞬切进循环
    if (chatPetMode === 'run' && chatPetKey === key) { return; }
    var prev = chatPetKey;
    chatPetMode = 'run';
    chatPetKey = key;
    chatPetKeyAt = Date.now();
    chatPetClearTimer();
    if (key === 'tool' && prev !== 'tool')
    {
        chatPetShow('before-tool');
        chatPetTimer = setTimeout(function ()
        {
            chatPetTimer = null;
            if (chatPetMode === 'run' && chatPetKey === 'tool') { chatPetShow('loop-tool', true); }
        }, CHAT_PET_BEFORE_TOOL_MS);
        return;
    }
    chatPetShow(chatPetPhaseMap[key]);
}

function chatPetEnterOffline()
{
    // 断线态——SSE 不通即显错图；恢复由引导层 onopen 置位后重新同步
    if (chatPetMode === 'offline') { return; }
    chatPetClearTimer();
    chatPetMode = 'offline';
    chatPetKey = 'offline';
    chatPetKeyAt = Date.now();
    chatPetShow('loop-sseErr');
}

// [段4] 空闲行为（intro / base / hook / check / sleep）
function chatPetEnterIdle()
{
    // 进入空闲——从活跃态回来先播收尾过渡（before-idle）再进基础待机；初载直接基础待机
    if (chatPetMode === 'idle') { return; }
    var fromRun = (chatPetMode === 'run');
    chatPetMode = 'idle';
    chatPetKey = 'idle';
    chatPetKeyAt = Date.now();
    chatPetClearTimer();
    if (fromRun)
    {
        chatPetIdleMode = 'intro';
        chatPetShow('before-idle');
        chatPetTimer = setTimeout(function ()
        {
            chatPetTimer = null;
            if (chatPetMode === 'idle' && chatPetIdleMode === 'intro') { chatPetPlayBase(true); }
        }, CHAT_PET_BEFORE_IDLE_MS);
        return;
    }
    chatPetPlayBase(false);
}

function chatPetPlayBase(instant)
{
    // 基础待机——loop-idle1-4 随机一张播 8s，到点直接进入睡（不再换下一张）
    chatPetIdleMode = 'base';
    var idx = chatPetPick(CHAT_PET_IDLE_BASE.length, chatPetIdleIdx);
    chatPetIdleIdx = idx;
    chatPetClearTimer();
    chatPetShow(CHAT_PET_IDLE_BASE[idx], instant === true);
    chatPetTimer = setTimeout(function ()
    {
        chatPetTimer = null;
        if (chatPetMode === 'idle' && chatPetIdleMode === 'base') { chatPetStartSleep(); }
    }, CHAT_PET_IDLE_BASE_HOLD_MS);
}

function chatPetIdleHook(on)
{
    // 鼠标移入/移出（仅空闲态响应——运行态优先，桌宠在陪干活）
    if (chatPetMode !== 'idle') { return; }
    if (on)
    {
        if (chatPetIdleMode === 'hook') { return; }
        chatPetIdleMode = 'hook';
        chatPetClearTimer();
        chatPetShow('before-idle-hook');
        chatPetTimer = setTimeout(function ()
        {
            chatPetTimer = null;
            if (chatPetMode === 'idle' && chatPetIdleMode === 'hook') { chatPetShow('loop-idle-hook', true); }
        }, CHAT_PET_BEFORE_HOOK_MS);
        return;
    }
    if (chatPetIdleMode === 'hook') { chatPetPlayBase(false); }
}

function chatPetIdleCheck()
{
    // 鼠标点击——after-idle-check1-5 随机一个播一遍；结束后按悬停状态回 hook 或基础待机
    if (chatPetMode !== 'idle') { return; }
    chatPetIdleMode = 'check';
    var idx = chatPetPick(CHAT_PET_CHECKS.length, chatPetCheckIdx);
    chatPetCheckIdx = idx;
    chatPetClearTimer();
    chatPetShow(CHAT_PET_CHECKS[idx].name, true);   // loop → after：瞬切
    chatPetTimer = setTimeout(function ()
    {
        chatPetTimer = null;
        if (chatPetMode !== 'idle' || chatPetIdleMode !== 'check') { return; }
        if (chatPetHovered) { chatPetIdleMode = ''; chatPetIdleHook(true); }
        else { chatPetPlayBase(false); }
    }, CHAT_PET_CHECKS[idx].ms);
}

function chatPetStartSleep()
{
    // 入睡——before-idle-sleep 播一遍后瞬切进睡眠循环（唤醒：鼠标移入或态变化）
    chatPetIdleMode = 'sleep';
    chatPetClearTimer();
    chatPetShow('before-idle-sleep');
    chatPetTimer = setTimeout(function ()
    {
        chatPetTimer = null;
        if (chatPetMode === 'idle' && chatPetIdleMode === 'sleep') { chatPetShow('loop-idle-sleep', true); }
    }, CHAT_PET_BEFORE_SLEEP_MS);
}

// [段5] 对外接口
function chatPetSync()
{
    // 状态同步——由 chatRenderStatus 汇聚点回调（对话页每次状态渲染后）
    if (chatPetImgs[0] === null) { return; }
    if (chatPetOffline) { chatPetEnterOffline(); return; }
    var st = '';
    if (window.chatRunState !== undefined && window.chatRunState !== null && window.chatRunState.state)
    {
        st = window.chatRunState.state;
    }
    if (st !== '' && chatPetPhaseMap[st] !== undefined) { chatPetEnterRun(st); return; }
    chatPetEnterIdle();
}

function chatPetSetOffline(on)
{
    // 断线置位/恢复——引导层 EventSource onerror/onopen 调用
    chatPetOffline = (on === true);
    chatPetSync();
}

function chatPetPreload()
{
    // 预加载——首屏不阻塞（延迟启动），保证态切换零闪烁
    for (var i = 0; i < chatPetFiles.length; i = i + 1)
    {
        var img = new Image();
        img.src = 'pet/' + chatPetFiles[i] + '.webp';
    }
}

function chatPetInit()
{
    // 初始化——引导层调用；容器缺失时静默退出（防御式，不影响对话主功能）
    chatPetImgs = [document.getElementById('chatPetImgA'), document.getElementById('chatPetImgB')];
    var box = document.getElementById('chatPet');
    if (chatPetImgs[0] === null || chatPetImgs[1] === null || box === null) { return; }
    // 鼠标交互（移入 hook / 点击 check / 按下拖动改位置）
    box.addEventListener('mouseenter', function () { chatPetHovered = true; chatPetIdleHook(true); });
    box.addEventListener('mouseleave', function () { chatPetHovered = false; chatPetIdleHook(false); });
    box.addEventListener('click', function ()
    {
        if (chatPetDragMoved) { return; }   // 拖动收尾的 click 不算摸头
        chatPetIdleCheck();
    });
    box.addEventListener('mousedown', chatPetDragStart);
    document.addEventListener('mousemove', chatPetDragMove);
    document.addEventListener('mouseup', chatPetDragEnd);
    chatPetSync();
    setTimeout(chatPetPreload, 1000);
}

function chatPetDragStart(e)
{
    // 按下——记录抓取偏移；right/bottom 定位换算为 left/top（此后以左上角定位）
    var box = document.getElementById('chatPet');
    if (box === null) { return; }
    var r = box.getBoundingClientRect();
    chatPetDragMoved = false;
    chatPetDrag = { dx: e.clientX - r.left, dy: e.clientY - r.top, x0: e.clientX, y0: e.clientY };
    box.style.left = r.left + 'px';
    box.style.top = r.top + 'px';
    box.style.right = 'auto';
    box.style.bottom = 'auto';
    e.preventDefault();
}

function chatPetDragMove(e)
{
    // 拖动——位移超阈值才算拖动（避免手抖吃掉摸头点击）；限制在视口内
    if (chatPetDrag === null) { return; }
    var box = document.getElementById('chatPet');
    if (box === null) { return; }
    var moved = Math.abs(e.clientX - chatPetDrag.x0) + Math.abs(e.clientY - chatPetDrag.y0);
    if (chatPetDragMoved === false && moved < 4) { return; }
    chatPetDragMoved = true;
    var x = e.clientX - chatPetDrag.dx;
    var y = e.clientY - chatPetDrag.dy;
    var maxX = window.innerWidth - box.offsetWidth;
    var maxY = window.innerHeight - box.offsetHeight;
    if (x < 0) { x = 0; }
    if (y < 0) { y = 0; }
    if (x > maxX) { x = maxX; }
    if (y > maxY) { y = maxY; }
    box.style.left = x + 'px';
    box.style.top = y + 'px';
}

function chatPetDragEnd()
{
    // 释放——结束拖动（moved 标志保留至下次按下，供 click 判据）
    chatPetDrag = null;
}
