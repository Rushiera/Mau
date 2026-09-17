// CH4 外观层——chat-pet.js：桌宠渲染（纯前端；对话页的组成部分）
// 定位：读后端权威运行态（chatRunState——六态）→ 态映射动画资源 → 调度空闲行为与交互反应
// 数据源：chat-core.js 的 chatRunState（经 chatRenderStatus 汇聚点回调）；SSE 断线由引导层 onerror/onopen 置位
// 铁律：前端零业务逻辑——本模块只做「态 / 交互 → 资源」映射与动画调度，不产生业务事件、不介入会话状态机
// 资源：html/pet/*.webp（286x256 原尺寸 1:1 显示 · 20ms/帧 · 透明底 · 圆角矩形边缘羽化；源素材 Workspace/pet/*.gif）
//
// 形态语义（素材约定）：
//   loop-*  循环态（link/wait/think/tool/run/reply/sseErr/idle/idle-hook/idle-sleep）
//   before-* 过渡动画（播一遍）——before-idle-hook 移入 / before-idle-sleep 入睡
//            · before-idle 实为 **after-reply 语义**（整轮结束的收尾动作）——只在「活跃态 → 空闲」时播一次
//            · before-tool 已废弃——素材姿态与 loop-tool 对不齐，进工具态直接切循环图
//   after-*  后置反应（播一遍）——after-idle-check 点击后的摸头反应
// 空闲节律（莎 2026-09-16 定）：进空闲随机一张基础待机播 8s → 直接进入睡；hook/check 结束后同样回到这个 8s 周期
// 渐隐规则（莎 2026-09-16 定）：before → loop、loop → after 两段是连续动作——**瞬切**；其余切换走 0.25s 交叉溶解
// 素材加载（莎 2026-09-17 定）：初始化即串行加载 → blob 持有 + Cache API 缓存（UI 版本键——版本变即失效）
//   · 加载门禁——首图 loop-sseErr 就位前桌宠不启动（保持不可见，不挂破图）；就位后常显该图作加载态；全量就绪才开放调度
//   · blob 持有 = 断线（宿主不可达）时零网络依赖；缓存 = 刷新不重下（限带宽/穿透场景关键）
//   · 缓存失效键 = 每张素材的内容哈希（js/pet-manifest.js）——改图/改名只重下变动那一张，与代码版本无关
//   · 硬保底——右上角「清理缓存」按钮主动清桶（脏数据 / 改图不改名时用）
//   · 播放时长 = manifest 单一真相（免前端常量与素材漂移）；接缝溶解名单盖住姿态不接的 before→loop 对

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
var chatPetRes = {};              // 素材持有表（名称 → blob URL）——串行加载逐张填入；有值则断线时零网络依赖
var chatPetLoading = true;        // 加载门禁——true 期间不开放调度（不响应态变化，不跑空闲节律）
var chatPetBooted = false;        // 首图（loop-sseErr）是否已就位——未就位桌宠保持不可见
var chatPetFail = 0;              // 加载失败计数——全量结束后汇总告警（不静默）
var CHAT_PET_CACHE_PREFIX = 'ch4-pet';    // Cache API 桶名（固定——失效靠逐张 URL 的内容哈希，不靠换桶名）
var chatPetManifest = (typeof chatPetManifestData !== 'undefined') ? chatPetManifestData : {};   // 素材清单（名称 → {h:内容哈希, ms:总时长}）——来自 js/pet-manifest.js；缺失降级空表
// 接缝溶解名单——before 末帧与 loop 首帧姿态不接的对，强制走溶解盖接缝（实测最优截断改善 <10%，裁切无用）
// · loop-idle——before-idle 收尾动作的落点（素材 2026-09-17 由三张合并为一张），姿态不保证对齐，故走溶解
// · before-tool 已废弃（素材删除，进 tool 态直切 loop-tool，无接缝）；其余对仍走瞬切
var CHAT_PET_SEAM_BLEND = {
    'loop-idle-sleep': true, 'loop-idle-hook': true, 'loop-idle': true
};

// 六态 → 循环资源
var chatPetPhaseMap = { link: 'loop-link', wait: 'loop-wait', think: 'loop-think', tool: 'loop-tool', run: 'loop-run', reply: 'loop-reply' };

var CHAT_PET_IDLE_BASE = ['loop-idle'];   // 基础待机（单张——素材 2026-09-17 由三张合并为一张；pick 逻辑兼容任意张数）
var CHAT_PET_CHECKS = [
    { name: 'after-idle-check', ms: 580 }
];
var CHAT_PET_IDLE_BASE_HOLD_MS = 5000;   // 基础待机时长——5s 到点进入睡（前端节律，非素材时长；2026-09-17 由 8s 调整）
// 入睡过渡遍数——before-idle-sleep 单遍 1980ms；取 2 遍 ≈ 3.96s（「5s 待机 → 约 3s 过渡 → 睡」，
// 且切点落在整遍边界不跳帧；实时长以 manifest 为准）
var CHAT_PET_BEFORE_SLEEP_LOOPS = 2;
// 以下为素材播放时长的**兜底值**——实际以 manifest 为准（chatPetMs）；素材重转后无需改这里
var CHAT_PET_BEFORE_IDLE_MS = 1980;      // before-idle 兜底时长
var CHAT_PET_BEFORE_HOOK_MS = 1140;      // before-idle-hook 兜底时长
var CHAT_PET_BEFORE_SLEEP_MS = 1980;     // before-idle-sleep 兜底时长

// 全量资源名（预加载清单——须与 html/pet/ 目录一致）
var chatPetFiles = [
    'loop-link', 'loop-wait', 'loop-think', 'loop-tool', 'loop-run', 'loop-reply', 'loop-sseErr',
    'before-idle',
    'loop-idle',
    'loop-idle-hook', 'before-idle-hook',
    'loop-idle-sleep', 'before-idle-sleep',
    'after-idle-check'
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
    // 换图——useInstant 时瞬切（连续动作），否则双层交叉溶解 0.25s
    // 接缝溶解名单内的目标强制溶解（姿态不接，瞬切会跳帧）
    if (chatPetImgs[0] === null) { return; }
    if (chatPetCur === name) { return; }
    var useInstant = (instant === true) && (CHAT_PET_SEAM_BLEND[name] !== true);
    chatPetCur = name;
    chatPetLastInstant = useInstant;
    var cur = chatPetImgs[chatPetLayer];
    var next = chatPetImgs[1 - chatPetLayer];
    if (useInstant === true)
    {
        next.style.transition = 'none';
        cur.style.transition = 'none';
    }
    next.src = chatPetSheet(name);
    next.style.opacity = '1';
    cur.style.opacity = '0';
    if (useInstant === true)
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
    // 活跃态——六态直接映射循环资源（before-tool 前置已废弃：素材姿态与循环图对不齐）
    if (chatPetMode === 'run' && chatPetKey === key) { return; }
    chatPetMode = 'run';
    chatPetKey = key;
    chatPetKeyAt = Date.now();
    chatPetClearTimer();
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
    // 进入空闲——活跃态回空闲（= 整轮结束）先播 before-idle（after-reply 收尾动作）再进基础待机；
    // 加载完成首次、断线恢复回空闲均不播收尾（fromRun 判据）
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
        }, chatPetMs('before-idle', CHAT_PET_BEFORE_IDLE_MS));
        return;
    }
    chatPetPlayBase(false);
}

function chatPetPlayBase(instant)
{
    // 基础待机——loop-idle 播 8s，到点直接进入睡（素材单张；多张时按 pick 轮转）
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
        }, chatPetMs('before-idle-hook', CHAT_PET_BEFORE_HOOK_MS));
        return;
    }
    if (chatPetIdleMode === 'hook') { chatPetPlayBase(false); }
}

function chatPetIdleCheck()
{
    // 鼠标点击——after-idle-check 播一遍；结束后按悬停状态回 hook 或基础待机
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
    }, chatPetMs(CHAT_PET_CHECKS[idx].name, CHAT_PET_CHECKS[idx].ms));
}

function chatPetStartSleep()
{
    // 入睡——before-idle-sleep 播**整遍**（共 CHAT_PET_BEFORE_SLEEP_LOOPS 遍）后瞬切进睡眠循环
    chatPetIdleMode = 'sleep';
    chatPetClearTimer();
    chatPetShow('before-idle-sleep');
    chatPetTimer = setTimeout(function ()
    {
        chatPetTimer = null;
        if (chatPetMode === 'idle' && chatPetIdleMode === 'sleep') { chatPetShow('loop-idle-sleep', true); }
    }, chatPetMs('before-idle-sleep', CHAT_PET_BEFORE_SLEEP_MS) * CHAT_PET_BEFORE_SLEEP_LOOPS);
}

// [段5] 对外接口
function chatPetSync()
{
    // 状态同步——由 chatRenderStatus 汇聚点回调（对话页每次状态渲染后）
    if (chatPetImgs[0] === null) { return; }
    if (chatPetLoading === true) { return; }   // 加载门禁——未就绪不调度（保持 sseErr 加载态）
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

function chatPetUrl(name)
{
    // 素材 URL——带内容哈希版本（改图/改名即换 URL，缓存自动失效且只重下变动那张）
    var m = chatPetManifest[name];
    if (m === undefined || m.h === undefined) { return 'pet/' + name + '.webp'; }
    return 'pet/' + name + '.webp?v=' + m.h;
}

function chatPetMs(name, fallback)
{
    // 素材播放时长——单一真相在 manifest（素材重转即跟随，前端不留漂移常量）
    var m = chatPetManifest[name];
    if (m === undefined || m.ms === undefined || m.ms <= 0) { return fallback; }
    return m.ms;
}

function chatPetSheet(name)
{
    // 换图用 URL——持有表命中用 blob（宿主不可达时仍可切图），未就绪回落网络路径
    var url = chatPetRes[name];
    if (url === undefined) { return chatPetUrl(name); }
    return url;
}

function chatPetLoadOrder()
{
    // 加载序——首图 loop-sseErr（加载态常显），其余按清单顺序
    var names = ['loop-sseErr'];
    for (var i = 0; i < chatPetFiles.length; i = i + 1)
    {
        if (chatPetFiles[i] !== 'loop-sseErr') { names.push(chatPetFiles[i]); }
    }
    return names;
}

function chatPetPruneEntries(cache, names)
{
    // 桶内清理——删掉不在当前清单里的条目（素材改名/删除后不留死条目）；清理失败不影响本次使用
    if (cache === null || cache === undefined) { return Promise.resolve(0); }
    var want = {};
    for (var i = 0; i < names.length; i = i + 1) { want[chatPetUrl(names[i])] = true; }
    return cache.keys().then(function (reqs)
    {
        var jobs = [];
        for (var j = 0; j < reqs.length; j = j + 1)
        {
            var k = reqs[j].url.indexOf('/pet/');
            if (k < 0) { continue; }
            if (want[reqs[j].url.substring(k + 1)] !== true) { jobs.push(cache.delete(reqs[j])); }
        }
        return Promise.all(jobs).then(function () { return jobs.length; });
    }).catch(function () { return 0; });
}

function chatPetOpenCache()
{
    // 缓存桶——固定名；无 Cache API 环境降级 null（每次走网络）
    if (typeof caches === 'undefined' || caches === null) { return Promise.resolve(null); }
    return caches.open(CHAT_PET_CACHE_PREFIX).catch(function () { return null; });
}

function chatPetFetch(url, cache)
{
    // 网络取回 + 回写缓存——非 2xx 不写（避免把 404 固化）；写缓存失败不致命
    return fetch(url).then(function (r)
    {
        if (r.ok === true && cache !== null && cache !== undefined)
        {
            try { cache.put(url, r.clone()).catch(function () { }); } catch (e) { }
        }
        return r;
    });
}

function chatPetLoadOne(name, cache)
{
    // 单张素材——缓存命中直取（零网络）；未命中走网络。返回 Promise（resolve=名称）
    var url = chatPetUrl(name);
    var get = null;
    if (cache !== null && cache !== undefined)
    {
        get = cache.match(url).then(function (hit)
        {
            if (hit === undefined || hit === null) { return chatPetFetch(url, cache); }
            return hit;
        });
    }
    else
    {
        get = chatPetFetch(url, cache);
    }
    return get.then(function (r)
    {
        if (r.ok !== true) { throw new Error('HTTP ' + r.status); }
        return r.blob();
    }).then(function (b)
    {
        chatPetRes[name] = URL.createObjectURL(b);
        return name;
    });
}

function chatPetBoot()
{
    // 首图就位后的启动——桌宠显形并常显 sseErr 作加载态；首图失败则保持不可见（不挂破图）
    if (chatPetBooted === true) { return; }
    chatPetBooted = true;
    chatPetMode = 'loading';
    chatPetKey = 'loading';
    chatPetKeyAt = Date.now();
    if (chatPetRes['loop-sseErr'] !== undefined) { chatPetShow('loop-sseErr'); }
}

function chatPetLoadDone()
{
    // 全量结束——解除门禁并开放调度；有失败则汇总告警（具名不静默）
    chatPetLoading = false;
    if (chatPetFail > 0) { uiWarn('桌宠素材', new Error('加载失败 ' + chatPetFail + ' 张——相关态回落网络路径')); }
    chatPetSync();
}

function chatPetLoadSeq(names, i, cache)
{
    // 串行逐张——前后不并发（限带宽/高延迟场景不拥塞，卡点可定位）；失败继续下一张
    if (i >= names.length)
    {
        chatPetLoadDone();
        return;
    }
    chatPetLoadOne(names[i], cache)
        .then(function ()
        {
            if (i === 0) { chatPetBoot(); }
            chatPetLoadSeq(names, i + 1, cache);
        })
        .catch(function (e)
        {
            chatPetFail = chatPetFail + 1;
            uiWarn('桌宠素材 ' + names[i], e);
            if (i === 0) { chatPetBoot(); }
            chatPetLoadSeq(names, i + 1, cache);
        });
}

function chatPetLoadAll()
{
    // 加载总入口——清单随脚本同步就位（无需异步取）；无加载通道（非浏览器环境）直接解除门禁
    if (typeof fetch !== 'function') { chatPetLoading = false; return; }
    if (typeof URL.createObjectURL !== 'function') { chatPetLoading = false; return; }
    var names = chatPetLoadOrder();
    chatPetOpenCache().then(function (cache)
    {
        chatPetPruneEntries(cache, names);
        chatPetLoadSeq(names, 0, cache);
    });
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
    var elClear = document.getElementById('chatCacheClear');
    if (elClear !== null) { elClear.addEventListener('click', chatPetClearCache); }
    chatPetLoadAll();
}

function chatPetDragStart(e)
{
    // 按下——记录抓取偏移；right/bottom 定位换算为 left/top（此后以左上角定位）
    if (chatPetLoading === true) { return; }   // 加载门禁——未启动前不可拖
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

function chatPetTip(msg)
{
    // 轻量提示——复用顶部状态位（缺失则退到 console）
    var el = document.getElementById('chatInfo');
    if (el !== null) { el.textContent = msg; return; }
    if (typeof console !== 'undefined' && console.log) { console.log('[桌宠] ' + msg); }
}

function chatPetClearCache()
{
    // 硬保底——清空素材缓存桶（改图不改名 / 缓存脏数据时的主动恢复手段）
    if (typeof caches === 'undefined' || caches === null)
    {
        chatPetTip('本环境不支持缓存');
        return;
    }
    caches.keys().then(function (keys)
    {
        var jobs = [];
        for (var i = 0; i < keys.length; i = i + 1)
        {
            if (keys[i].indexOf(CHAT_PET_CACHE_PREFIX) === 0) { jobs.push(caches.delete(keys[i])); }
        }
        return Promise.all(jobs).then(function () { return jobs.length; });
    }).then(function (n)
    {
        chatPetTip('素材缓存已清理（' + n + ' 桶）——刷新后重新加载');
    }).catch(function (e) { uiWarn('桌宠缓存清理', e); });
}
