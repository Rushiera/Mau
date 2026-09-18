// CH4 外观层——chat-tools.js：工具块渲染（骨架层 + 逐工具填充单例 + 回落）
// 定位：工具卡内部渲染的唯一实现——规格 Project/CH4/design-ch4-frontend-tools.md
// 三层：骨架（语义大类 → 壳）· 填充（工具一对一 → 肉）· 回落（调用方兜底，保持现行渲染）
// 纪律：纯函数、无状态、无网络、无 innerHTML（文本一律 textContent）；未登记工具返回 null → 调用方走现行渲染（零回归）
// 加载顺序：chat-md.js → chat-cmd.js → chat-tools.js → chat-view.js → chat-core.js（chat.html 引导层）

// ═══════════════════════════════════════════
// 阈值与共用小件
// ═══════════════════════════════════════════

var CHAT_SEG_FOLD_CHARS = 1000;   // 段内折叠阈值——字符（沿用既有 1000 口径）
var CHAT_SEG_FOLD_LINES = 15;     // 段内折叠阈值——行

// 规模描述——「1.50k 字符 / 42 行」（大数走 chatFmtCount，与折叠行后缀同源）
function chatSegSize(text) {
    var t = text || '';
    var lines = (t.length === 0) ? 0 : t.split('\n').length;
    return chatFmtCount(t.length) + ' 字符 / ' + lines + ' 行';
}

// 段落——details.seg（二级折叠：点折叠头展开全量；限高滚动由 CSS 承担）
function chatSeg(cls, cap, fill) {
    var det = document.createElement('details');
    det.className = 'seg ' + cls;
    var sum = document.createElement('summary');
    sum.className = 'seg-cap';
    sum.textContent = cap;
    det.appendChild(sum);
    var body = document.createElement('div');
    body.className = 'seg-body';
    fill(body);
    det.appendChild(body);
    return det;
}

// 段内文本块——cls 沿用既有锚点类（.ta / .tr / .ta.warn），父子关系变化不影响选择器
function chatSegBlock(parent, cls, text) {
    var el = document.createElement('div');
    el.className = cls;
    el.textContent = text || '';
    parent.appendChild(el);
    return el;
}

// JSON 容错解析——空串 / 非对象 / 截断 → null（调用方回落原文，不静默丢弃）
function chatTryJson(text) {
    var t = (text || '').replace(/^\s+|\s+$/g, '');
    if (t.length === 0 || t.charAt(0) !== '{') { return null; }
    try { return JSON.parse(t); } catch (e) { return null; }
}

// 取字符串字段——类型不符返回空串
function chatField(obj, key) {
    if (!obj || typeof obj[key] !== 'string') { return ''; }
    return obj[key];
}

// ═══════════════════════════════════════════
// 工具填充单例——每工具一支（对齐宿主 ToolSummaryFormatter 的「一处分派 + 一工具一支 Fmt_*」）
// 返回 {tag, tagCls, segs}：tag = 折叠行工具变体标签（可空）；segs = 段数组
// ═══════════════════════════════════════════

// powershell / powershell7——exec 骨架：exit 徽标 + stdout / stderr 分段 + 截断警示
// 两版本显式区分（2026-09-18）：折叠行版本标签（PS 5.1 / PS 7）+ 版本色 + 类型图标（💻 / 💠）
function chatFillPs(tool) {
    var is7 = (tool.name === 'powershell7');
    var args = chatTryJson(tool.arguments) || {};
    var cmd = chatField(args, 'command');
    var cwd = chatField(args, 'cwd');
    var r = chatTryJson(tool.result);
    var segs = [];

    // [段1] 输入——命令原文（完整可见）+ cwd（有则前置一行）
    var inParts = [];
    if (cwd.length > 0) { inParts.push('cwd: ' + cwd); }
    if (cmd.length > 0) { inParts.push(cmd); }
    var inText = (inParts.length > 0) ? inParts.join('\n') : (tool.arguments || '');
    var inCap = '输入 · 命令 ' + cmd.length + ' 字符' + (cwd.length > 0 ? ' · 指定 cwd' : '');
    segs.push(chatSeg('seg-in', inCap, function (body) {
        chatSegBlock(body, 'ta', inText);
    }));

    // [段2] 输出——exit 徽标 + stdout / stderr 分段（各自规模标注）+ 截断 / 超时 / 空输出显式
    var outCap = '输出';
    var warnText = '';
    var blocks = [];
    if (tool.result === undefined) {
        outCap = '输出 · 处理中';
        blocks.push({ cap: '', cls: 'ta', text: '⏳ 处理中…' });
    } else if (r && typeof r.exit !== 'undefined') {
        var out = chatField(r, 'stdout');
        var err = chatField(r, 'stderr');
        outCap = '输出 · exit ' + r.exit;
        if (out.length > 0) { blocks.push({ cap: 'stdout · ' + chatSegSize(out), cls: 'tr', text: out }); }
        if (err.length > 0) { blocks.push({ cap: 'stderr · ' + chatSegSize(err), cls: 'tr err', text: err }); }
        if (out.length === 0 && err.length === 0) { outCap = outCap + ' · 无输出'; }
        if (r.truncated === true) {
            outCap = outCap + ' · ⚠️ 已截断';
            warnText = '⚠️ 输出已达上限被截断——后续内容未回传' + (r.timeout === true ? '；进程超时已终止' : '');
        }
    } else {
        // 非 JSON 结果（异常面 / 旧格式）——原文可见，不静默
        outCap = '输出 · 原始输出';
        blocks.push({ cap: '', cls: 'tr', text: tool.result || '' });
    }
    segs.push(chatSeg('seg-out', outCap, function (body) {
        if (warnText.length > 0) { chatSegBlock(body, 'ta warn', warnText); }
        for (var i = 0; i < blocks.length; i = i + 1) {
            if (blocks[i].cap.length > 0) {
                var sec = document.createElement('div');
                sec.className = 'seg-sec';
                var cap = document.createElement('div');
                cap.className = 'seg-sec-cap';
                cap.textContent = blocks[i].cap;
                sec.appendChild(cap);
                chatSegBlock(sec, blocks[i].cls, blocks[i].text);
                body.appendChild(sec);
            } else {
                chatSegBlock(body, blocks[i].cls, blocks[i].text);
            }
        }
        if (blocks.length === 0 && warnText.length === 0) { chatSegBlock(body, 'tr', '（无输出）'); }
    }));

    return { tag: is7 ? 'PS 7' : 'PS 5.1', tagCls: is7 ? 'ps7' : 'ps5', segs: segs };
}

// 填充注册表——新增工具在此加一支；不加则 chatToolBody 返回 null（调用方回落现行渲染）
var CHAT_TOOL_FILLERS = {
    'powershell': chatFillPs,
    'powershell7': chatFillPs
};

// ═══════════════════════════════════════════
// 分派入口——chat-view.js chatToolCard 消费
// ═══════════════════════════════════════════

// 工具块内部渲染——命中填充注册表则由骨架产出段数组；未登记返回 null（调用方走现行渲染）
// tool = toolcard 载荷（name / arguments / result / summary / toolIndex / toolTotal）
function chatToolBody(tool) {
    if (!tool || typeof tool.name !== 'string') { return null; }
    var fill = CHAT_TOOL_FILLERS[tool.name];
    if (typeof fill !== 'function') { return null; }
    return fill(tool);
}
