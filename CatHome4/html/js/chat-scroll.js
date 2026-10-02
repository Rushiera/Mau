// CH4 外观层——chat-scroll.js：对话区滚动自持层（规格 design-ch4-frontend-scroll.md）
// 两节：§一 滚轮兜底转发（前端最核心操作行为的默认通道——豁免登记制，非逐元素白名单）
//       §二 自绘滚动带（IDE 风格：视口指示块 + 消息刻度 + 点击跳转 + 拖拽；替代原生滚动条）
// 加载顺序：须在 chat-core.js 之后（#chatMsgs 已就位）；本文件自持 DOM 引用，不依赖其它模块的全局变量
// 单向数据流：本文件只读视图块 DOM（.chat-row），不产生消息、不改业务状态
// 坐标系：带体高度 ↔ 内容高度（同一比例）——指示块高度 = 视口占比，刻度 = 每条导航消息

// ============ DOM 引用 ============
var chatScrollMsgs = document.getElementById('chatMsgs');
var chatBandEl = document.getElementById('chatScrollBand');
var chatBandThumbEl = document.getElementById('sbThumb');
var chatBandTicksEl = document.getElementById('sbTicks');
// 导航项表——{ row: 行元素, role: 'user' | 'reply' }；重建时机 = chatMsgs 子节点变化
var chatBandItems = [];
var chatBandDragging = false;
// 属性变化重建节流句柄（展开/折叠高频，合并到一帧后处理）
var chatBandRebuildTimer = null;
// 指示块最小高度——内容极长时仍可抓取
var CHAT_BAND_MIN_THUMB = 24;
// 刻度最小高——真实像素高比例算出的值过小时保底可见
var CHAT_BAND_MIN_TICK = 2;
// 刻度命中半径（点击刻度线本身太细，用坐标就近判定）
var CHAT_BAND_TICK_HIT = 6;

// ============ §一 滚轮兜底转发 ============
// 设计：滚轮默认视为「滚动对话区」——鼠标落在任何非滚动面上都生效（两侧空白 / 标题栏 / 状态条 / 按钮区）。
// 豁免面 = 真正需要自己纵向滚动的面。🔴 登记制：新增可滚动面必须在此登记一行，
// 否则该面的滚轮会被本层吞掉（转去滚对话区）。登记记录见规格文档 §二豁免登记表。
var CHAT_WHEEL_EXEMPT = '.note-popover, .delay-popover, .chat-think .ct-body, .chat-think .ct-full, textarea';

/**
 * 滚轮增量归一——像素 / 行 / 页三态
 * @param {number} deltaY 原始增量
 * @param {number} deltaMode 0=像素 1=行 2=页
 * @param {number} pageH 页高（deltaMode=2 时用，缺省回落 400）
 * @returns {number} 像素增量
 */
function chatWheelDelta(deltaY, deltaMode, pageH)
{
    var d = deltaY || 0;
    if (deltaMode === 1)
    {
        return d * 16;
    }
    if (deltaMode === 2)
    {
        return d * (pageH > 0 ? pageH : 400);
    }
    return d;
}

/**
 * 兜底转发——目标不在豁免面、也不在对话区内时接管滚轮并驱动对话区
 * @param {Object} e 滚轮事件（需 target / deltaY / deltaMode / ctrlKey / preventDefault）
 */
function chatWheelForward(e)
{
    if (!chatScrollMsgs || !e)
    {
        return;
    }
    if (e.ctrlKey === true)
    {
        // Ctrl+滚轮 = 浏览器缩放——不接管
        return;
    }
    var t = e.target;
    if (t && typeof t.closest === 'function')
    {
        if (t.closest(CHAT_WHEEL_EXEMPT))
        {
            // 豁免面自己滚
            return;
        }
        if (t.closest('#chatMsgs'))
        {
            // 对话区内——走原生滚动
            return;
        }
    }
    if (typeof e.preventDefault === 'function')
    {
        e.preventDefault();
    }
    chatScrollMsgs.scrollTop = chatScrollMsgs.scrollTop
        + chatWheelDelta(e.deltaY, e.deltaMode, chatScrollMsgs.clientHeight);
}

// ============ §二 自绘滚动带 ============

/**
 * 导航项收集——判据用 DOM 语义（实时与历史两条渲染路径同一判据；不依赖 msgIndex——user 实时事件不带该字段）
 * 收：user 行 + assistant 正文行；排除：工具卡 / 思考块 / 重试 / 错误 / 注入报告 / 轮末统计 / 静默提示 / 中止提示
 * @returns {Array} [{ row, role }]
 */
function chatNavCollect()
{
    var out = [];
    if (!chatScrollMsgs)
    {
        return out;
    }
    var rows = chatScrollMsgs.querySelectorAll('.chat-row');
    for (var i = 0; i < rows.length; i = i + 1)
    {
        var row = rows[i];
        var bubble = null;
        for (var j = 0; j < row.children.length; j = j + 1)
        {
            if (row.children[j].classList && row.children[j].classList.contains('chat-bubble'))
            {
                bubble = row.children[j];
                break;
            }
        }
        if (bubble === null)
        {
            continue;
        }
        if (row.classList.contains('user'))
        {
            out.push({ row: row, role: 'user' });
            continue;
        }
        if (!row.classList.contains('assistant'))
        {
            continue;
        }
        if (bubble.classList.contains('roundsum'))
        {
            // 本轮结算块——独立刻度类（All 态淡紫）
            out.push({ row: row, role: 'sum' });
            continue;
        }
        if (bubble.classList.contains('tool'))
        {
            // 工具卡——展开 / 折叠两档入带（run 态先行卡除外）
            var toolRole = chatToolBandRole(bubble);
            if (toolRole.length > 0)
            {
                out.push({ row: row, role: toolRole });
            }
            continue;
        }
        if (bubble.classList.contains('reason'))
        {
            // 思考块——展开档 / 压缩档两档入带（流式态除外）
            var thinkRole = chatThinkBandRole(bubble);
            if (thinkRole.length > 0)
            {
                out.push({ row: row, role: thinkRole });
            }
            continue;
        }
        if (chatNavExcluded(bubble))
        {
            continue;
        }
        out.push({ row: row, role: 'reply' });
    }
    return out;
}

/**
 * 工具卡入带角色——run 态先行卡（pending）不入带；其余按展开 / 折叠分两档
 * @param {Element} bubble 工具卡气泡
 * @returns {string} 'tool'（展开）/ 'tool-fold'（折叠）/ ''（不入带）
 */
function chatToolBandRole(bubble)
{
    if (bubble.classList.contains('pending'))
    {
        return '';
    }
    var det = bubble.querySelector('details');
    if (det === null)
    {
        return '';
    }
    return (det.open === true) ? 'tool' : 'tool-fold';
}

/**
 * 思考块入带角色——流式态不入带；完成态按展开档 / 压缩档分两档
 * @param {Element} bubble 思考块气泡
 * @returns {string} 'think'（展开档）/ 'think-fold'（压缩档）/ ''（不入带）
 */
function chatThinkBandRole(bubble)
{
    var tk = bubble.querySelector('.chat-think');
    if (tk === null)
    {
        return '';
    }
    if (tk.classList.contains('stream'))
    {
        return '';
    }
    if (!tk.classList.contains('done'))
    {
        return '';
    }
    return tk.classList.contains('full') ? 'think' : 'think-fold';
}

/**
 * 非导航块判定——按气泡类名排除（重试 / 错误 / 注入报告 / 静默提示 / 中止提示）
 * @param {Element} bubble 气泡元素
 * @returns {boolean} true=不参与导航
 */
function chatNavExcluded(bubble)
{
    var skip = ['retry', 'error', 'inject', 'stall', 'paused'];
    for (var i = 0; i < skip.length; i = i + 1)
    {
        if (bubble.classList.contains(skip[i]))
        {
            return true;
        }
    }
    return false;
}

/**
 * 指示块几何——纯函数
 * @param {number} scrollTop 当前滚动位置
 * @param {number} scrollHeight 内容高
 * @param {number} clientHeight 视口高
 * @param {number} bandH 带体高
 * @returns {Object} { top, height }
 */
function chatBandThumb(scrollTop, scrollHeight, clientHeight, bandH)
{
    if (scrollHeight <= 0 || clientHeight <= 0 || bandH <= 0)
    {
        return { top: 0, height: 0 };
    }
    var h = bandH * (clientHeight / scrollHeight);
    if (h < CHAT_BAND_MIN_THUMB)
    {
        h = CHAT_BAND_MIN_THUMB;
    }
    if (h > bandH)
    {
        h = bandH;
    }
    var max = scrollHeight - clientHeight;
    var top = 0;
    if (max > 0)
    {
        var r = scrollTop / max;
        if (r < 0)
        {
            r = 0;
        }
        if (r > 1)
        {
            r = 1;
        }
        top = (bandH - h) * r;
    }
    return { top: top, height: h };
}

/**
 * 刻度纵坐标——内容坐标 → 带体坐标（同一比例）
 * @param {number} contentY 块在内容坐标系中的顶部位置
 * @param {number} scrollHeight 内容高
 * @param {number} bandH 带体高
 * @returns {number} 带体内纵坐标
 */
function chatBandTickY(contentY, scrollHeight, bandH)
{
    if (scrollHeight <= 0 || bandH <= 0)
    {
        return 0;
    }
    var y = bandH * (contentY / scrollHeight);
    if (y < 0)
    {
        y = 0;
    }
    if (y > bandH - 2)
    {
        y = bandH - 2;
    }
    return y;
}

/**
 * 带体比例 → scrollTop（点击 / 拖拽定位）
 * @param {number} ratio 带体坐标比例（0=顶 1=底）
 * @param {number} scrollHeight 内容高
 * @param {number} clientHeight 视口高
 * @returns {number} 目标 scrollTop
 */
function chatBandRatioToScroll(ratio, scrollHeight, clientHeight)
{
    var max = scrollHeight - clientHeight;
    if (max <= 0)
    {
        return 0;
    }
    var r = ratio;
    if (r < 0)
    {
        r = 0;
    }
    if (r > 1)
    {
        r = 1;
    }
    return r * max;
}

/**
 * 块在内容坐标系中的顶部位置（与布局方式无关的通用算法）
 * @param {Element} el 目标元素
 * @returns {number} 内容坐标 y
 */
function chatBandContentY(el)
{
    if (!chatScrollMsgs || !el)
    {
        return 0;
    }
    var base = chatScrollMsgs.getBoundingClientRect().top - chatScrollMsgs.scrollTop;
    return el.getBoundingClientRect().top - base;
}

/**
 * 带体几何同步——位置与高度对齐对话区矩形（fixed 浮层，随标题栏 / 输入区高度变化自动跟随）
 */
function chatBandLayout()
{
    if (!chatBandEl || !chatScrollMsgs)
    {
        return;
    }
    var rect = chatScrollMsgs.getBoundingClientRect();
    chatBandEl.style.top = Math.round(rect.top) + 'px';
    chatBandEl.style.height = Math.round(rect.height) + 'px';
    var overflow = chatScrollMsgs.scrollHeight - chatScrollMsgs.clientHeight;
    if (rect.height <= 0 || overflow <= 1)
    {
        // 内容未溢出——整带隐藏（机制必需，非审美阈值）
        chatBandEl.classList.remove('on');
        return;
    }
    chatBandEl.classList.add('on');
    chatBandSyncThumb();
    chatBandSyncTicks();
}

/**
 * 指示块同步——滚动时唯一更新项（O(1)，不遍历刻度）
 */
function chatBandSyncThumb()
{
    if (!chatBandThumbEl || !chatScrollMsgs || !chatBandEl)
    {
        return;
    }
    var bandH = chatBandHeight();
    if (bandH <= 0)
    {
        return;
    }
    var g = chatBandThumb(chatScrollMsgs.scrollTop, chatScrollMsgs.scrollHeight, chatScrollMsgs.clientHeight, bandH);
    chatBandThumbEl.style.top = Math.round(g.top) + 'px';
    chatBandThumbEl.style.height = Math.round(g.height) + 'px';
}

/**
 * 刻度几何——块真实像素高按比例映射 + 最小高保底 + 不越过下一条（纯函数）
 * @param {number} contentY 本条内容坐标
 * @param {number} realH 本条真实像素高（DOM 实测）
 * @param {number} nextY 下一条内容坐标（-1=无下一条——用带体底作上限）
 * @param {number} scrollHeight 内容总高
 * @param {number} bandH 带体高
 * @returns {Object} { top, height }
 */
function chatBandTickGeom(contentY, realH, nextY, scrollHeight, bandH)
{
    if (scrollHeight <= 0 || bandH <= 0)
    {
        return { top: 0, height: 0 };
    }
    var top = chatBandTickY(contentY, scrollHeight, bandH);
    var h = bandH * (realH / scrollHeight);
    if (h < CHAT_BAND_MIN_TICK)
    {
        h = CHAT_BAND_MIN_TICK;
    }
    var limit = bandH - top;
    if (nextY >= 0)
    {
        // 不越过下一条——相邻刻度至少留 1px 间隔
        limit = chatBandTickY(nextY, scrollHeight, bandH) - top - 1;
    }
    if (limit < CHAT_BAND_MIN_TICK)
    {
        // 极端密集（间距不足最小高）——保最小高优先，允许轻微相接
        limit = CHAT_BAND_MIN_TICK;
    }
    if (h > limit)
    {
        h = limit;
    }
    return { top: top, height: h };
}

/**
 * 刻度几何同步——内容或尺寸变化时重建（滚动时不走此路径）
 * 高度 = 块真实像素高 × 带体高 / 内容总高（整条带 = 全部内容的等比缩略图）
 */
function chatBandSyncTicks()
{
    if (!chatBandTicksEl || !chatScrollMsgs)
    {
        return;
    }
    var bandH = chatBandHeight();
    if (bandH <= 0)
    {
        return;
    }
    var sh = chatScrollMsgs.scrollHeight;
    var kids = chatBandTicksEl.children;
    var n = chatBandItems.length;
    var tops = [];
    for (var i = 0; i < n; i = i + 1)
    {
        tops.push(chatBandContentY(chatBandItems[i].row));
    }
    for (var j = 0; j < kids.length && j < n; j = j + 1)
    {
        var nextY = (j + 1 < n) ? tops[j + 1] : -1;
        var g = chatBandTickGeom(tops[j], chatBandItems[j].row.getBoundingClientRect().height, nextY, sh, bandH);
        kids[j].style.top = Math.round(g.top) + 'px';
        kids[j].style.height = Math.round(g.height) + 'px';
    }
}

/**
 * 带体高——优先实际布局高，回落内联样式（jsdom 无布局时取样式值）
 * @returns {number} 像素高
 */
function chatBandHeight()
{
    if (!chatBandEl)
    {
        return 0;
    }
    if (chatBandEl.clientHeight > 0)
    {
        return chatBandEl.clientHeight;
    }
    return parseFloat(chatBandEl.style.height) || 0;
}

/**
 * 节流重建——属性变化（展开 / 折叠）高频，合并到一帧后处理
 */
function chatBandScheduleRebuild()
{
    if (chatBandRebuildTimer !== null)
    {
        return;
    }
    chatBandRebuildTimer = setTimeout(function ()
    {
        chatBandRebuildTimer = null;
        chatBandBuildTicks();
    }, 50);
}

/**
 * 刻度重建——导航项表刷新 + 刻度元素重建 + 全量几何同步
 */
function chatBandBuildTicks()
{
    chatBandItems = chatNavCollect();
    if (chatBandTicksEl)
    {
        chatBandTicksEl.textContent = '';
        for (var i = 0; i < chatBandItems.length; i = i + 1)
        {
            var d = document.createElement('div');
            d.className = 'sb-tick ' + chatBandItems[i].role;
            d.setAttribute('data-idx', String(i));
            chatBandTicksEl.appendChild(d);
        }
    }
    chatBandLayout();
}

/**
 * 按带体纵坐标定位——指示块中心对准光标（拖拽手感）
 * @param {number} clientY 视口纵坐标
 */
function chatBandSeekFromY(clientY)
{
    if (!chatBandEl || !chatScrollMsgs)
    {
        return;
    }
    var rect = chatBandEl.getBoundingClientRect();
    var bandH = chatBandHeight();
    if (bandH <= 0)
    {
        return;
    }
    var g = chatBandThumb(chatScrollMsgs.scrollTop, chatScrollMsgs.scrollHeight, chatScrollMsgs.clientHeight, bandH);
    var span = bandH - g.height;
    var r = 0;
    if (span > 0)
    {
        r = (clientY - rect.top - g.height / 2) / span;
    }
    chatScrollMsgs.scrollTop = chatBandRatioToScroll(r, chatScrollMsgs.scrollHeight, chatScrollMsgs.clientHeight);
}

/**
 * 刻度就近命中——光标 ±6px 内最近刻度（点击刻度线的替代判据）
 * @param {number} clientY 视口纵坐标
 * @returns {number} 刻度序号（-1=未命中）
 */
function chatBandTickIndexAt(clientY)
{
    if (!chatBandEl || !chatScrollMsgs)
    {
        return -1;
    }
    var bandH = chatBandHeight();
    if (bandH <= 0)
    {
        return -1;
    }
    var rect = chatBandEl.getBoundingClientRect();
    var sh = chatScrollMsgs.scrollHeight;
    var best = -1;
    var bestD = CHAT_BAND_TICK_HIT;
    for (var i = 0; i < chatBandItems.length; i = i + 1)
    {
        var y = chatBandTickY(chatBandContentY(chatBandItems[i].row), sh, bandH) + rect.top;
        var d = Math.abs(y - clientY);
        if (d <= bestD)
        {
            bestD = d;
            best = i;
        }
    }
    return best;
}

/**
 * 跳转到第 idx 条导航消息（顶部留 8px 余量）
 * @param {number} idx 导航项序号
 */
function chatBandJumpTo(idx)
{
    if (!chatScrollMsgs || idx < 0 || idx >= chatBandItems.length)
    {
        return;
    }
    chatScrollMsgs.scrollTop = chatBandContentY(chatBandItems[idx].row) - 8;
}

/**
 * 带体按下——命中刻度则跳该条，否则按比例定位并进入拖拽
 * @param {Object} e 指针事件
 */
function chatBandOnDown(e)
{
    if (!chatBandEl || !chatScrollMsgs)
    {
        return;
    }
    var idx = chatBandTickIndexAt(e.clientY);
    if (idx >= 0)
    {
        chatBandJumpTo(idx);
    }
    else
    {
        chatBandSeekFromY(e.clientY);
    }
    chatBandDragging = true;
    chatBandEl.classList.add('dragging');
    if (typeof chatBandEl.setPointerCapture === 'function' && e.pointerId !== undefined)
    {
        chatBandEl.setPointerCapture(e.pointerId);
    }
    if (typeof e.preventDefault === 'function')
    {
        e.preventDefault();
    }
}

/**
 * 带体拖动——拖拽中持续按纵坐标定位
 * @param {Object} e 指针事件
 */
function chatBandOnMove(e)
{
    if (!chatBandDragging)
    {
        return;
    }
    chatBandSeekFromY(e.clientY);
}

/**
 * 带体释放——结束拖拽
 */
function chatBandOnUp()
{
    chatBandDragging = false;
    if (chatBandEl)
    {
        chatBandEl.classList.remove('dragging');
    }
}

/**
 * 初始化——滚轮兜底注册 + 带体事件绑定 + 观察器（子节点变化重建刻度 / 尺寸变化重排）
 */
function chatBandInit()
{
    document.addEventListener('wheel', chatWheelForward, { passive: false });
    if (chatScrollMsgs)
    {
        chatScrollMsgs.addEventListener('scroll', chatBandSyncThumb);
        if (typeof MutationObserver === 'function')
        {
            // 子节点变化（新块 / 清空）——节流重建（A143：原实现立即全量重建，流式期间每建一块都遍历全部行）
            var mo = new MutationObserver(function () { chatBandScheduleRebuild(); });
            mo.observe(chatScrollMsgs, { childList: true });
            // 展开 / 折叠态变化（details[open]）——节流重建（属性变化高频）；
            // A143 收窄：不再观察 class——流式进行中标记每增量改 class，曾导致每增量全量重建刻度；
            // 思考块高度档切换等 class 场景改由调用方显式走 chatBandScheduleRebuild()
            var ma = new MutationObserver(function () { chatBandScheduleRebuild(); });
            ma.observe(chatScrollMsgs, { attributes: true, subtree: true, attributeFilter: ['open'] });
        }
        if (typeof ResizeObserver === 'function')
        {
            var ro = new ResizeObserver(function () { chatBandLayout(); });
            ro.observe(chatScrollMsgs);
        }
        if (typeof window !== 'undefined' && typeof window.addEventListener === 'function')
        {
            // 视口尺寸变化——带体几何跟随（resize 只到 window，不到 document）
            window.addEventListener('resize', chatBandLayout);
        }
    }
    if (chatBandEl)
    {
        chatBandEl.addEventListener('pointerdown', chatBandOnDown);
    }
    // 拖拽三事件挂 document——指针捕获失败 / 拖出带体时仍能收到（拖拽门控在 chatBandOnMove / chatBandOnUp）
    document.addEventListener('pointermove', chatBandOnMove);
    document.addEventListener('pointerup', chatBandOnUp);
    document.addEventListener('pointercancel', chatBandOnUp);
    chatBandBuildTicks();
}

chatBandInit();
