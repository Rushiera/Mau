// CH4 外观层——panel-res.js：资源占用区（A254 · design-ch4-pytools.md §七）
// 依赖：app.js 先加载；渲染入口 renderRes(res) 由 app.js applySnapshot 调用。
// 数据源：快照 res 段——每秒一次固定刷新参（ResProbe 采集：本体进程 / py 服务 / GPU 显存）。
// 曲线为前端自缓存自绘制（后端不存历史）；保留最近 180 点（1 秒一点 = 3 分钟），F5 刷新清空重来。

var resInfoEl = document.getElementById('resInfo');
var resProcEl = document.getElementById('resProc');
var resLoopEl = document.getElementById('resLoop');
var resPyEl = document.getElementById('resPy');
var resGpuEl = document.getElementById('resGpu');
var resPauseBtn = document.getElementById('resPause');

// 主循环分段名 → 展示名（与宿主 LoopTiming 的段名一一对应）
var RES_SEG_NAMES = {
    tick: 'Flow Tick',
    http: 'HTTP 快照泵',
    hosts: 'Admin 宿主泵',
    sessions: '会话泵',
    catqueue: '猫队列泵',
    qqbot: 'QQBot',
    misc: '其它（重启 / 托盘 / 自启轮询）',
    'cs.summary': '会话·轮末总结轮询',
    'cs.pause': '会话·中止收尾',
    'cs.idle': '会话·空闲检查 / 轮首',
    'cs.llm': '会话·LLM 轮询',
    'cs.tool': '会话·工具批轮询',
    'cs.close': '会话·轮末收口',
    'cs.delays': '会话·延迟队列泵'
};

// 曲线窗口——180 点（样本上限；超出丢最旧）
var RES_WINDOW = 180;

// 历史缓存——{ wsMb, gpuPct }（F5 重置归零）
var resHistory = [];

// 采样判重——同一时刻的采样只处理一次（applySnapshot 每帧都会被调用）
var resLastT = -1;

// 曲线暂停标记——暂停后仍更新数字块，只是不再追加曲线点
var resPaused = false;

/**
 * 字节 → MB 文本（0 或不可读 → '-'）
 * @param {number} bytes 字节数
 * @returns {string} 展示文本
 */
function fmtResMb(bytes) {
    var v = Number(bytes) || 0;
    if (v <= 0) { return '-'; }
    return (v / 1048576).toFixed(1) + ' MB';
}

/**
 * 建一行——名称 + 值（两列，与依赖页签行同款观感）
 * @param {string} name 名称
 * @param {string} value 值文本
 * @returns {HTMLElement} 行元素
 */
function buildResRow(name, value) {
    var row = document.createElement('div');
    row.style.cssText = 'display:flex;align-items:center;gap:10px;padding:6px 12px;border-bottom:1px solid var(--ch-line-faint)';
    var label = document.createElement('span');
    label.textContent = name;
    label.style.cssText = 'flex:0 0 150px;color:var(--ch-fg-strong)';
    row.appendChild(label);
    var val = document.createElement('span');
    val.textContent = value;
    val.style.cssText = 'flex:1;min-width:0;color:var(--ch-fg-weak);font-size:var(--ch-fs-tag);overflow:hidden;text-overflow:ellipsis;white-space:nowrap';
    row.appendChild(val);
    return row;
}

/**
 * 渲染资源截面——数字块 + 曲线缓存追加 + 重绘
 * @param {object} res 快照 res 段（t / proc / py / gpu）
 */
function renderRes(res) {
    if (!res || !resProcEl) { return; }
    if (res.t === resLastT) { return; }   // 同一采样只处理一次
    resLastT = res.t;

    renderResProc(res.proc || {}, res.loop || {});
    renderResLoop(res.loop || {});
    renderResPy(res.py || []);
    renderResGpu(res.gpu || {});

    // 曲线缓存——暂停时不追加（数字块照常更新）
    if (!resPaused) {
        var proc = res.proc || {};
        var gpu = res.gpu || {};
        var loop = res.loop || {};
        var wsMb = (Number(proc.ws) || 0) / 1048576;
        var gpuPct = (Number(gpu.pct) || 0);
        var loopMs = (Number(loop.avgMs) || 0);
        resHistory.push({ wsMb: wsMb, gpuPct: gpuPct, loopMs: loopMs });
        while (resHistory.length > RES_WINDOW) { resHistory.shift(); }
    }
    drawResCharts();
}

/**
 * 本体进程块——工作集 / 私有字节 / 托管堆分代 / 主循环帧耗时
 * @param {object} proc 快照 proc 段
 * @param {object} loop 快照 loop 段（主循环帧耗时窗口统计）
 */
function renderResProc(proc, loop) {
    resProcEl.textContent = '';
    resProcEl.appendChild(buildResRow('工作集（WS）', fmtResMb(proc.ws)));
    resProcEl.appendChild(buildResRow('私有字节', fmtResMb(proc.private)));
    resProcEl.appendChild(buildResRow('托管堆', fmtResMb(proc.heap)));
    resProcEl.appendChild(buildResRow('已提交（GC committed）', fmtResMb(proc.committed)));
    resProcEl.appendChild(buildResRow('分代 G0 / G1 / G2 / LOH',
        fmtResMb(proc.g0) + ' / ' + fmtResMb(proc.g1) + ' / ' + fmtResMb(proc.g2) + ' / ' + fmtResMb(proc.loh)));
    resProcEl.appendChild(buildResRow('主循环帧耗时（均值 / 峰值）',
        (Number(loop.avgMs) || 0).toFixed(2) + ' / ' + (Number(loop.peakMs) || 0).toFixed(2) + ' ms'));
}

/**
 * 主循环分段块——逐段一行（展示名 / 均值 / 峰值）
 * @param {object} loop 快照 loop 段（含 segments 数组）
 */
function renderResLoop(loop) {
    if (!resLoopEl) { return; }
    resLoopEl.textContent = '';
    // 超帧统计（2026-10-16）——帧预算 / 实测帧率 / 累计超帧 / 上一秒超帧与占比
    var budgetMs = Number(loop.budgetMs) || 0;
    var secFrames = Number(loop.secFrames) || 0;
    var secOverruns = Number(loop.secOverruns) || 0;
    var secPct = Number(loop.secOverrunPct) || 0;
    resLoopEl.appendChild(buildResRow('帧预算 / 实测帧率', budgetMs + ' ms / ' + secFrames + ' fps'));
    resLoopEl.appendChild(buildResRow('超帧（累计 / 上一秒）',
        (Number(loop.overrunTotal) || 0) + ' / ' + secOverruns + '（占上一秒 ' + secPct.toFixed(1) + ' %）'));
    var segs = loop.segments || [];
    if (segs.length === 0) {
        resLoopEl.appendChild(buildResRow('（无）', '尚无采样'));
        return;
    }
    for (var i = 0; i < segs.length; i = i + 1) {
        var s = segs[i];
        var label = RES_SEG_NAMES[s.name] || s.name;
        resLoopEl.appendChild(buildResRow(label,
            (Number(s.avgMs) || 0).toFixed(2) + ' / ' + (Number(s.peakMs) || 0).toFixed(2) + ' ms'));
    }
}

/**
 * py 服务块——逐服务一行（名称 / 状态 / PID / 工作集）
 * @param {Array} list 快照 py 段
 */
function renderResPy(list) {
    resPyEl.textContent = '';
    if (list.length === 0) {
        resPyEl.appendChild(buildResRow('（无）', '未声明服务或 IPyService 未注入'));
        return;
    }
    for (var i = 0; i < list.length; i = i + 1) {
        var s = list[i];
        var state = s.alive ? '运行中' : '未启动';
        var detail = state + (s.pid > 0 ? (' · pid=' + s.pid) : '') + ' · ' + fmtResMb(s.ws);
        resPyEl.appendChild(buildResRow(s.name || '(未命名)', detail));
    }
}

/**
 * GPU 块——已用 / 总量 / 占比（WDDM 下按进程显存不可得，只报全局）
 * @param {object} gpu 快照 gpu 段
 */
function renderResGpu(gpu) {
    resGpuEl.textContent = '';
    if (!gpu.available) {
        resGpuEl.appendChild(buildResRow('（不可用）', 'nvidia-smi 未提供数据'));
        return;
    }
    resGpuEl.appendChild(buildResRow('显存已用 / 总量', gpu.usedMib + ' / ' + gpu.totalMib + ' MiB'));
    resGpuEl.appendChild(buildResRow('显存占比', gpu.pct + ' %'));
}

// ── 折线绘制（零依赖：canvas 手绘；宽度随容器，保留最近 180 点）──
/**
 * 画一条曲线——自适应宽度 + 上下留白 + 网格基线；少于两点不画
 * @param {string} canvasId 画布元素 id
 * @param {Array} values 数值序列
 * @param {string} color 线色
 */
function drawResLine(canvasId, values, color) {
    var cv = document.getElementById(canvasId);
    if (!cv) { return; }
    var w = cv.clientWidth || 600;
    var h = cv.clientHeight || 64;
    var dpr = window.devicePixelRatio || 1;
    var pw = Math.floor(w * dpr);
    var ph = Math.floor(h * dpr);
    if (cv.width !== pw || cv.height !== ph) {
        cv.width = pw;
        cv.height = ph;
    }
    var ctx = cv.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    ctx.strokeStyle = '#88888833';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(0, h - 1.5);
    ctx.lineTo(w, h - 1.5);
    ctx.stroke();
    if (values.length < 2) { return; }
    var min = values[0];
    var max = values[0];
    for (var i = 1; i < values.length; i = i + 1) {
        if (values[i] < min) { min = values[i]; }
        if (values[i] > max) { max = values[i]; }
    }
    var span = max - min;
    if (span <= 0) { span = 1; }
    var pad = 4;
    var usable = h - pad * 2;
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    for (var j = 0; j < values.length; j = j + 1) {
        var x = values.length === 1 ? 0 : (j * w / (values.length - 1));
        var y = pad + usable - ((values[j] - min) / span) * usable;
        if (j === 0) { ctx.moveTo(x, y); } else { ctx.lineTo(x, y); }
    }
    ctx.stroke();
    // 峰值 / 当前值标注（左上 / 右上）
    ctx.fillStyle = '#888888';
    ctx.font = '11px sans-serif';
    ctx.fillText('峰值 ' + max.toFixed(1), 4, 12);
    ctx.fillText('当前 ' + values[values.length - 1].toFixed(1), Math.max(4, w - 80), 12);
}

/**
 * 重绘两条曲线——本体内存（MB）+ GPU 占比（%）
 */
function drawResCharts() {
    var mem = [];
    var gpu = [];
    var loop = [];
    for (var i = 0; i < resHistory.length; i = i + 1) {
        mem.push(resHistory[i].wsMb);
        gpu.push(resHistory[i].gpuPct);
        loop.push(resHistory[i].loopMs);
    }
    drawResLine('resChartMem', mem, '#4a9eff');
    drawResLine('resChartGpu', gpu, '#ffa64a');
    drawResLine('resChartLoop', loop, '#7ec97e');
    if (resInfoEl) {
        var total = resHistory.length;
        resInfoEl.textContent = '资源占用——本体进程 / py 服务 / GPU 显存（每秒采样；曲线 ' + total + '/' + RES_WINDOW + ' 点，约 ' + (total / 60).toFixed(1) + ' 分钟）';
    }
}

// 暂停按钮——只停曲线追加，数字块照常刷新
if (resPauseBtn) {
    resPauseBtn.addEventListener('click', function () {
        resPaused = !resPaused;
        resPauseBtn.textContent = resPaused ? '继续曲线' : '暂停曲线';
    });
}

// 窗口尺寸变化——重绘（画布宽度随容器自适应）
window.addEventListener('resize', function () { drawResCharts(); });
