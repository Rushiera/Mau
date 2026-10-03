// ═══════════════════════════════════════════
// chat/fx/scroll.js —— 对话区滚动自持层（独立功能件 A168）
//
// 规格：design-ch4-frontend-scroll.md —— §一 滚轮兜底转发 · §二 自绘滚动带
// 启动：由 fx/registry.js 的 fxBoot 统一调用 chatScrollInit（脚本加载不自启，DOM 已就绪）
//     本件自持 DOM 引用，不依赖其它件的全局变量
//
// 最简口径（2026-10-03 莎定）：
//   · 对话区行只有两种形态——**气泡**（.chat-bubble）与**普通条**（工具卡 .chat-tool · 思考块 .chat-think）
//     → 刻度分类走单一判据 scrollTickRole()，不复刻旧实现的展开 / 折叠分档与排除表
//   · 基本算法与渲染规则继承旧实现——几何比例映射（带体高 ↔ 内容高）+ 既有 CSS .sb-tick 规则（零新增样式）
//   · #chatMsgs.scrollTop 是唯一滚动执行器——本件只读写它，不产生消息、不改业务状态
// ═══════════════════════════════════════════

// ── DOM 引用 ─────────────────────────────────
var bandMsgs = document.getElementById('chatMsgs');
var bandBox = document.getElementById('chatScrollBand');
var bandThumb = document.getElementById('sbThumb');
var bandTicks = document.getElementById('sbTicks');

// ── 常量 ─────────────────────────────────────
var BAND_MIN_THUMB = 24;    // 指示块最小高——内容极长时仍可抓取
var BAND_MIN_TICK = 2;      // 刻度最小高——比例算出的值过小时保底可见
var BAND_TICK_HIT = 6;      // 刻度命中半径——刻度线仅 2px，点击走带体坐标就近判定
var BAND_REBUILD_MS = 50;   // 刻度重建节流——展开 / 折叠属高频属性变化，合并到一帧后处理

// ── 状态 ─────────────────────────────────────
var bandItems = [];              // 刻度表 [{ row, role }]——重建时机 = 对话区子节点 / 展开态变化
var bandDragging = false;
var bandRebuildTimer = null;

// ═══ §一 滚轮兜底转发 ═══
// 滚轮默认视为「滚动对话区」——鼠标落在任何非滚动面上都生效（两侧空白 / 标题栏 / 状态条 / 按钮区）。
// 🔴 豁免登记制：新增可滚动面必须在此登记一行，否则该面的滚轮会被本层吞掉（转去滚对话区）。
var WHEEL_EXEMPT = '.note-popover, .delay-popover, .chat-think .ct-body, .chat-think .ct-full, textarea';

/// 滚轮增量归一——像素 / 行 / 页三态（页高缺省 400）
function wheelDelta(deltaY, deltaMode, pageH) {
    var d = deltaY || 0;
    if (deltaMode === 1) {
        return d * 16;
    }
    if (deltaMode === 2) {
        return d * (pageH > 0 ? pageH : 400);
    }
    return d;
}

/// 兜底转发——目标不在豁免面、也不在对话区内时接管滚轮，驱动对话区
function wheelForward(e) {
    if (!bandMsgs || !e) {
        return;
    }
    if (e.ctrlKey === true) {
        // Ctrl+滚轮 = 浏览器缩放——不接管
        return;
    }
    var t = e.target;
    if (t && typeof t.closest === 'function') {
        if (t.closest(WHEEL_EXEMPT)) {
            // 豁免面自己滚
            return;
        }
        if (t.closest('#chatMsgs')) {
            // 对话区内——走原生滚动
            return;
        }
    }
    if (typeof e.preventDefault === 'function') {
        e.preventDefault();
    }
    bandMsgs.scrollTop = bandMsgs.scrollTop + wheelDelta(e.deltaY, e.deltaMode, bandMsgs.clientHeight);
}

// ═══ §二 自绘滚动带 ═══

/// 刻度角色——单一判据，按行的语义类取既有 CSS 规则：
///   气泡两色（user / reply）· 普通条两色（tool / think）· 结算（sum）
function scrollTickRole(row) {
    if (row.classList.contains('user')) {
        return 'user';
    }
    if (row.classList.contains('roundsum')) {
        return 'sum';
    }
    if (row.classList.contains('tool')) {
        return 'tool';
    }
    if (row.classList.contains('reason')) {
        return 'think';
    }
    return 'reply';
}

/// 刻度收集——对话区每个 .chat-row 一条刻度（推送序即渲染序，本件不排序、不过滤）
function bandCollect() {
    var out = [];
    if (!bandMsgs) {
        return out;
    }
    var rows = bandMsgs.querySelectorAll('.chat-row');
    for (var i = 0; i < rows.length; i = i + 1) {
        out.push({ row: rows[i], role: scrollTickRole(rows[i]) });
    }
    return out;
}

/// 指示块几何——视口框高 = 视口占比，位置按滚动比例（纯函数）
function bandThumbGeom(scrollTop, scrollHeight, clientHeight, bandH) {
    if (scrollHeight <= 0 || clientHeight <= 0 || bandH <= 0) {
        return { top: 0, height: 0 };
    }
    var h = bandH * (clientHeight / scrollHeight);
    if (h < BAND_MIN_THUMB) {
        h = BAND_MIN_THUMB;
    }
    if (h > bandH) {
        h = bandH;
    }
    var max = scrollHeight - clientHeight;
    var top = 0;
    if (max > 0) {
        var r = scrollTop / max;
        if (r < 0) {
            r = 0;
        }
        if (r > 1) {
            r = 1;
        }
        top = (bandH - h) * r;
    }
    return { top: top, height: h };
}

/// 刻度纵坐标——内容坐标 → 带体坐标（同一比例）
function bandTickY(contentY, scrollHeight, bandH) {
    if (scrollHeight <= 0 || bandH <= 0) {
        return 0;
    }
    var y = bandH * (contentY / scrollHeight);
    if (y < 0) {
        y = 0;
    }
    if (y > bandH - 2) {
        y = bandH - 2;
    }
    return y;
}

/// 带体比例 → scrollTop（点击 / 拖拽定位）
function bandRatioToScroll(ratio, scrollHeight, clientHeight) {
    var max = scrollHeight - clientHeight;
    if (max <= 0) {
        return 0;
    }
    var r = ratio;
    if (r < 0) {
        r = 0;
    }
    if (r > 1) {
        r = 1;
    }
    return r * max;
}

/// 块在内容坐标系中的顶部位置——与布局方式无关的通用算法
function bandContentY(node) {
    if (!bandMsgs || !node) {
        return 0;
    }
    var base = bandMsgs.getBoundingClientRect().top - bandMsgs.scrollTop;
    return node.getBoundingClientRect().top - base;
}

/// 带体高——优先实际布局高，回落内联样式（无布局环境取样式值）
function bandHeight() {
    if (!bandBox) {
        return 0;
    }
    if (bandBox.clientHeight > 0) {
        return bandBox.clientHeight;
    }
    return parseFloat(bandBox.style.height) || 0;
}

/// 指示块同步——滚动时唯一更新项（O(1)，不遍历刻度）
function bandThumbSync() {
    if (!bandThumb || !bandMsgs) {
        return;
    }
    var h = bandHeight();
    if (h <= 0) {
        return;
    }
    var g = bandThumbGeom(bandMsgs.scrollTop, bandMsgs.scrollHeight, bandMsgs.clientHeight, h);
    bandThumb.style.top = Math.round(g.top) + 'px';
    bandThumb.style.height = Math.round(g.height) + 'px';
}

/// 刻度几何——块真实像素高按比例映射 + 最小高保底 + 不越过下一条（纯函数）
function bandTickGeom(contentY, realH, nextY, scrollHeight, bandH) {
    if (scrollHeight <= 0 || bandH <= 0) {
        return { top: 0, height: 0 };
    }
    var top = bandTickY(contentY, scrollHeight, bandH);
    var h = bandH * (realH / scrollHeight);
    if (h < BAND_MIN_TICK) {
        h = BAND_MIN_TICK;
    }
    var limit = bandH - top;
    if (nextY >= 0) {
        // 不越过下一条——相邻刻度至少留 1px 间隔
        limit = bandTickY(nextY, scrollHeight, bandH) - top - 1;
    }
    if (limit < BAND_MIN_TICK) {
        // 极端密集（间距不足最小高）——保最小高优先，允许轻微相接
        limit = BAND_MIN_TICK;
    }
    if (h > limit) {
        h = limit;
    }
    return { top: top, height: h };
}

/// 刻度几何同步——内容或尺寸变化时重建（滚动时不走此路径）
function bandTicksSync() {
    if (!bandTicks || !bandMsgs) {
        return;
    }
    var bandH = bandHeight();
    if (bandH <= 0) {
        return;
    }
    var sh = bandMsgs.scrollHeight;
    var kids = bandTicks.children;
    var n = bandItems.length;
    var tops = [];
    for (var i = 0; i < n; i = i + 1) {
        tops.push(bandContentY(bandItems[i].row));
    }
    for (var j = 0; j < kids.length && j < n; j = j + 1) {
        var nextY = (j + 1 < n) ? tops[j + 1] : -1;
        var g = bandTickGeom(tops[j], bandItems[j].row.getBoundingClientRect().height, nextY, sh, bandH);
        kids[j].style.top = Math.round(g.top) + 'px';
        kids[j].style.height = Math.round(g.height) + 'px';
    }
}

/// 带体几何同步——位置与高度对齐对话区矩形（fixed 浮层，随标题栏 / 输入区高度变化自动跟随）
function bandLayout() {
    if (!bandBox || !bandMsgs) {
        return;
    }
    var rect = bandMsgs.getBoundingClientRect();
    bandBox.style.top = Math.round(rect.top) + 'px';
    bandBox.style.height = Math.round(rect.height) + 'px';
    var overflow = bandMsgs.scrollHeight - bandMsgs.clientHeight;
    if (rect.height <= 0 || overflow <= 1) {
        // 内容未溢出——整带隐藏（机制必需，非审美阈值）
        bandBox.classList.remove('on');
        return;
    }
    bandBox.classList.add('on');
    bandThumbSync();
    bandTicksSync();
}

/// 节流重建——高频属性变化合并到一帧后处理
function bandScheduleRebuild() {
    if (bandRebuildTimer !== null) {
        return;
    }
    bandRebuildTimer = setTimeout(function () {
        bandRebuildTimer = null;
        bandRebuild();
    }, BAND_REBUILD_MS);
}

/// 刻度重建——刻度表刷新 + 刻度元素重建 + 全量几何同步
function bandRebuild() {
    bandItems = bandCollect();
    if (bandTicks) {
        bandTicks.textContent = '';
        for (var i = 0; i < bandItems.length; i = i + 1) {
            var d = document.createElement('div');
            d.className = 'sb-tick ' + bandItems[i].role;
            bandTicks.appendChild(d);
        }
    }
    bandLayout();
}

/// 按带体纵坐标定位——指示块中心对准光标（拖拽手感）
function bandSeek(clientY) {
    if (!bandBox || !bandMsgs) {
        return;
    }
    var rect = bandBox.getBoundingClientRect();
    var bandH = bandHeight();
    if (bandH <= 0) {
        return;
    }
    var g = bandThumbGeom(bandMsgs.scrollTop, bandMsgs.scrollHeight, bandMsgs.clientHeight, bandH);
    var span = bandH - g.height;
    var r = 0;
    if (span > 0) {
        r = (clientY - rect.top - g.height / 2) / span;
    }
    bandMsgs.scrollTop = bandRatioToScroll(r, bandMsgs.scrollHeight, bandMsgs.clientHeight);
}

/// 刻度就近命中——光标 ±6px 内最近刻度（刻度线太细，不挂指针事件）
function bandTickIndexAt(clientY) {
    if (!bandBox || !bandMsgs) {
        return -1;
    }
    var bandH = bandHeight();
    if (bandH <= 0) {
        return -1;
    }
    var rect = bandBox.getBoundingClientRect();
    var sh = bandMsgs.scrollHeight;
    var best = -1;
    var bestD = BAND_TICK_HIT;
    for (var i = 0; i < bandItems.length; i = i + 1) {
        var y = bandTickY(bandContentY(bandItems[i].row), sh, bandH) + rect.top;
        var d = Math.abs(y - clientY);
        if (d <= bestD) {
            bestD = d;
            best = i;
        }
    }
    return best;
}

/// 跳转到第 idx 条刻度（顶部留 8px 余量）
function bandJumpTo(idx) {
    if (!bandMsgs || idx < 0 || idx >= bandItems.length) {
        return;
    }
    bandMsgs.scrollTop = bandContentY(bandItems[idx].row) - 8;
}

/// 带体按下——命中刻度则跳该条，否则按比例定位并进入拖拽
function bandPointerDown(e) {
    if (!bandBox || !bandMsgs) {
        return;
    }
    var idx = bandTickIndexAt(e.clientY);
    if (idx >= 0) {
        bandJumpTo(idx);
    } else {
        bandSeek(e.clientY);
    }
    bandDragging = true;
    bandBox.classList.add('dragging');
    if (typeof bandBox.setPointerCapture === 'function' && e.pointerId !== undefined) {
        bandBox.setPointerCapture(e.pointerId);
    }
    if (typeof e.preventDefault === 'function') {
        e.preventDefault();
    }
}

/// 带体拖动——拖拽中持续按纵坐标定位
function bandPointerMove(e) {
    if (!bandDragging) {
        return;
    }
    bandSeek(e.clientY);
}

/// 带体释放——结束拖拽（高亮走 .dragging 类，与 hover 同源）
function bandPointerUp() {
    bandDragging = false;
    if (bandBox) {
        bandBox.classList.remove('dragging');
    }
}

/// 初始化——滚轮兜底注册 + 带体事件绑定 + 观察器（子节点 / 展开态变化重建，尺寸变化重排）
function chatScrollInit() {
    document.addEventListener('wheel', wheelForward, { passive: false });
    if (bandMsgs) {
        bandMsgs.addEventListener('scroll', bandThumbSync);
        if (typeof MutationObserver === 'function') {
            // 子节点变化（新块 / 清区重绘）——节流重建
            var mo = new MutationObserver(function () {
                bandScheduleRebuild();
            });
            mo.observe(bandMsgs, { childList: true });
            // 展开 / 折叠（details[open]）与高度档（思考块 .full）——节流重建
            var ma = new MutationObserver(function () {
                bandScheduleRebuild();
            });
            ma.observe(bandMsgs, { attributes: true, subtree: true, attributeFilter: ['open', 'class'] });
        }
        if (typeof ResizeObserver === 'function') {
            var ro = new ResizeObserver(function () {
                bandLayout();
            });
            ro.observe(bandMsgs);
        }
        if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
            window.addEventListener('resize', bandLayout);
        }
    }
    if (bandBox) {
        bandBox.addEventListener('pointerdown', bandPointerDown);
    }
    // 拖拽三事件挂 document——指针捕获失败 / 拖出带体时仍能收口
    document.addEventListener('pointermove', bandPointerMove);
    document.addEventListener('pointerup', bandPointerUp);
    document.addEventListener('pointercancel', bandPointerUp);
    bandRebuild();
}
