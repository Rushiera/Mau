// CH4 外观层——chat-view.js：对话渲染面（气泡/工具卡/思考块/注入报告/历史渲染/roundsum）——chat-core.js 拆分（2026-09-08 体量治理）
// 加载顺序：chat-md.js → chat-view.js → chat-core.js → chat-note.js（chat.html 引导层引用）
// 职责：纯渲染——气泡 DOM 构造 + 视图块 HTML；无状态无网络；状态/分发/指令在 chat-core.js
// 单向数据流铁律：本文件只被 chat-core.js 的事件分发调用，不自行产生消息

// 计数格式化——大数转 k/M（≥1000 → x.xx k；≥1000000 → x.xx M；M 为最大单位；保留两位小数；sessionId 等标识不适用）
function chatFmtCount(n) {
    n = Number(n) || 0;
    if (n >= 1000000) { return (n / 1000000).toFixed(2) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(2) + 'k'; }
    return String(n);
}

function chatBubble(role, cls) {
    var row = document.createElement('div');
    row.className = 'chat-row ' + role;
    var b = document.createElement('div');
    b.className = 'chat-bubble' + (cls ? ' ' + cls : '');
    row.appendChild(b);
    chatMsgs.appendChild(row);
    chatScrollBottom();
    return b;
}

function chatAppend(bubble, text) {
    // 专用文本节点追加——textContent 拼接会销毁子元素（details/工具卡被后续 text 事件清空——B4.2 根因）
    if (!bubble.textNode) {
        bubble.textNode = document.createTextNode('');
        bubble.insertBefore(bubble.textNode, bubble.firstChild);
    }
    bubble.textNode.data = bubble.textNode.data + text;
    chatScrollBottom();
}

// 气泡整块移除——行容器一并摘除（A85 think 流式块销毁 / A77 静默提示撤销共用）
function chatRemoveBubble(bubble) {
    if (!bubble || !bubble.parentNode) { return; }
    bubble.parentNode.removeChild(bubble);
}

// 工具图标——三级回落（2026-09-18 莎定）：工具专属（填充层）→ 骨架图标（默认）→ ❓（未登记）
// 未登记骨架的工具本不该出现——用问号显式暴露，不给通用图标掩盖
function chatToolIcon(name) {
    if (typeof chatToolIconOf === 'function') {
        return chatToolIconOf(name);
    }
    return '❓';
}

// 结果规模信息——ps 结果 JSON 解析（stdout/stderr 合计 + 截断/超时标记）；非 JSON 按纯文本长度
// 动机（2026-09-14）：消耗可见才能优化——16KB 硬截断保留，前端把规模与截断状态显式呈现
function chatResultInfo(resultText) {
    var text = resultText || '';
    var info = { chars: text.length, truncated: false, timeout: false };
    if (text.length === 0) {
        return info;
    }
    var trimmed = text.replace(/^\s+|\s+$/g, '');
    if (trimmed.charAt(0) !== '{') {
        return info;
    }
    try {
        var o = JSON.parse(trimmed);
        if (o && typeof o.stdout === 'string') {
            var n = o.stdout.length;
            if (typeof o.stderr === 'string') { n = n + o.stderr.length; }
            info.chars = n;
            info.truncated = (o.truncated === true);
            info.timeout = (o.timeout === true);
        }
    } catch (e) {
        // 非 JSON——按纯文本长度（info 已就绪）
    }
    return info;
}

// 折叠行消耗标注——大结果（≥1000 字符）或截断/超时才追加（小结果不标，避免噪音）
function chatResultSuffix(info) {
    if (!info) {
        return '';
    }
    if (info.truncated) {
        return ' · ⚠️ 已达上限 ' + chatFmtCount(info.chars) + ' 字符' + (info.timeout ? '（超时终止）' : '');
    }
    if (info.chars >= 1000) {
        return ' · ' + chatFmtCount(info.chars) + ' 字符';
    }
    return '';
}

// 展开态整块点击收起（2026-09-18）——原生 details 只有折叠头（summary）可点，展开后内容区长；
// 工具卡 / 思考块展开时整块任意位置点击即收起（折叠头仍走原生 toggle）
// 收起判据（四道关）：折叠头本身 · 交互元素（按钮/链接/输入）· 按下时已有选区 · 按下→抬起位移 <20px 且时长 <0.3s
function chatHasSelection() {
    if (typeof window.getSelection !== 'function') { return false; }
    var sel = window.getSelection();
    if (!sel || sel.isCollapsed === true) { return false; }
    return sel.toString().length > 0;
}

// 死区阈值——按下→抬起的位移/时长上限；超限视为拖选或长按，不触发收起
var CHAT_COLLAPSE_MAX_MOVE = 20;
var CHAT_COLLAPSE_MAX_MS = 300;

function chatBindBodyCollapse(det) {
    det.addEventListener('mousedown', function (e) {
        det._press = {
            x: e.clientX || 0,
            y: e.clientY || 0,
            t: Date.now(),
            sel: chatHasSelection()
        };
    });
    det.addEventListener('click', function (e) {
        if (det.open !== true) { return; }
        var t = e.target;
        if (t && typeof t.closest === 'function') {
            // 折叠头让位（外层 summary 原生 toggle / 内层段折叠头只切该段）+ 交互元素自处理；
            // 段内容点击仍收起整块——死区判据保护拖选与长按（2026-09-18 骨架层细化）
            if (t.closest('summary') || t.closest('button, a, input, textarea, select')) { return; }
        }
        var p = det._press;
        if (p) {
            det._press = null;
            if (p.sel === true) { return; }
            if (Math.abs((e.clientX || 0) - p.x) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if (Math.abs((e.clientY || 0) - p.y) > CHAT_COLLAPSE_MAX_MOVE) { return; }
            if ((Date.now() - p.t) > CHAT_COLLAPSE_MAX_MS) { return; }
        }
        det.open = false;
        if (det._updateSummary) { det._updateSummary(); }
    });
}

// A84——工具卡「运行中」占位行（单一出口）
// 起点（data-start）由 chat-core.js 在建先行卡时写入占位元素——宿主先行卡载荷无时间戳，已运行时长纯前端自算；
// 类名常量同源（渲染面加类 / 计时表选元素共用一处），chat-tools.js 骨架占位亦引用
var CHAT_PENDING_HOLD_CLS = 'pending-hold';

// 占位行文本——「⏳ 处理中…（已运行 12s）」；粒度 1 秒（整秒向下取整）
function chatPendingHoldText(ms) {
    var s = Math.floor(Math.max(0, ms) / 1000);
    return '⏳ 处理中…（已运行 ' + s + 's）';
}

function chatToolCard(tool, open) {
    // 工具卡——details 结构（open=true 展开：两段式先行卡直接展示 ⏳；缺省折叠——点击 summary 展开/收起）
    // .tn/.ta/.tr 类保留——测试与样式复用；result === undefined → 「⏳ 处理中…」占位（完成时整卡替换）
    // 失败态——ERR 前缀 或 结构化返回头 ok:false（chat-tools.js 单一出口；红色描边 + summary 转红）
    var isErr = (typeof chatIsErrResult === 'function') ? chatIsErrResult(tool.result) : (tool.result && tool.result.indexOf('ERR') === 0);
    var det = document.createElement('details');
    det.className = 'chat-tool' + (isErr ? ' err' : '');
    det.open = (open === true);
    var sum = document.createElement('summary');
    sum.className = 'tn';
    // 工具前缀——对齐 CH2：并发批次显示 [icon n/m]；单次保持现状（🔧/⚠️）
    var prefix;
    if (isErr) {
        prefix = (tool.toolTotal > 1) ? ('[⚠️ ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ') : '⚠️ ';
    } else if (tool.toolTotal > 1) {
        prefix = '[' + chatToolIcon(tool.name) + ' ' + (tool.toolIndex || '?') + '/' + tool.toolTotal + '] ';
    } else {
        // 单发同样用类型图标（原固定 🔧 扳手——2026-09-17）
        prefix = chatToolIcon(tool.name) + ' ';
    }
    // 可见性适配——powershell 命令硬解码为自然语言意图（chat-cmd.js；PS 双线不配 headline，折叠行由解读承担）
    var isPs = (tool.name === 'powershell' || tool.name === 'powershell7');
    // 折叠行文案来源（唯一产出在前端）：覆盖表 headline → 骨架兜底 → 工具名
    var summaryText = (typeof chatToolFallbackHeadline === 'function') ? chatToolFallbackHeadline(tool) : (tool.name || '?');
    // 覆盖表 headline——前端自然语言折叠行（2026-09-18 莎定）
    var hasHeadline = false;
    if (typeof chatToolHeadline === 'function') {
        var hl = chatToolHeadline(tool);
        if (hl.length > 0) { summaryText = hl; hasHeadline = true; }
    }
    var cmdIntent = null;
    if (isPs && typeof cmdDecodeTool === 'function') {
        cmdIntent = cmdDecodeTool(tool.arguments);
        if (cmdIntent) { summaryText = cmdIntent.brief; hasHeadline = false; }
        // 覆盖率采集——未识别命令段上报（异步 fire-and-forget；渲染零阻塞）
        if (cmdIntent && typeof cmdReportUnknown === 'function') { cmdReportUnknown(cmdIntent.unknown); }
    }
    var resultInfo = chatResultInfo(tool.result);
    // 骨架分派（chat-tools.js）——命中 → 段结构（含工具变体标签）；未登记 → null 走回落路径
    var body = (typeof chatToolBody === 'function') ? chatToolBody(tool) : null;
    sum.textContent = '';
    sum.appendChild(document.createTextNode(prefix));
    // A127——执行序徽标（宿主 order 表裁决；载荷缺 order 字段 = 旧块 / 未带——不渲染，零猜测）
    if (tool.order !== undefined && tool.order !== null && String(tool.order).length > 0) {
        var ordEl = document.createElement('span');
        ordEl.className = 'chat-order';
        ordEl.textContent = '⚙' + tool.order;
        sum.appendChild(ordEl);
        sum.appendChild(document.createTextNode(' '));
    }
    if (body && body.tag) {
        // 工具变体标签——双线工具的显式区分（如 PowerShell PS 5.1 / PS 7）
        var tagEl = document.createElement('span');
        tagEl.className = 'ps-tag ' + (body.tagCls || '');
        tagEl.textContent = body.tag;
        sum.appendChild(tagEl);
        sum.appendChild(document.createTextNode(' '));
    }
    sum.appendChild(document.createTextNode(summaryText + (hasHeadline ? '' : chatResultSuffix(resultInfo))));
    det.appendChild(sum);
    if (cmdIntent) {
        // 展开区首块——逐段意图对照（原文仍在下方 arguments 块；未识别段标 ❓）
        var ci = document.createElement('div');
        ci.className = 'cmd-intent';
        ci.textContent = cmdIntent.detail;
        det.appendChild(ci);
    }
    if (body && body.segs) {
        // 骨架路径——输入 / 输出段（二级折叠；段内沿用 .ta / .tr / .ta.warn 锚点类）
        for (var si = 0; si < body.segs.length; si++) {
            det.appendChild(body.segs[si]);
        }
    } else {
        // 回落路径——未登记工具保持现行渲染（零回归）
        if (tool.arguments) {
            var a = document.createElement('div');
            a.className = 'ta';
            a.textContent = tool.arguments;
            det.appendChild(a);
        }
        if (resultInfo.truncated) {
            // 截断警示——结果未完整回传（16KB 上限），消耗信号显式化
            var w = document.createElement('div');
            w.className = 'ta warn';
            w.textContent = '⚠️ 输出已达上限被截断——后续内容未回传' + (resultInfo.timeout ? '；进程超时已终止' : '');
            det.appendChild(w);
        }
        if (tool.result) {
            var r = document.createElement('div');
            r.className = 'tr' + (isErr ? ' err' : '');
            r.textContent = tool.result;
            det.appendChild(r);
        } else if (tool.result === undefined) {
            var w = document.createElement('div');
            w.className = 'ta ' + CHAT_PENDING_HOLD_CLS;
            w.textContent = chatPendingHoldText(0);
            det.appendChild(w);
        }
    }
    chatBindBodyCollapse(det);
    return det;
}

// A85——思考块渲染迁至 js/chat-think.js（两态两渲染器）；原 details 折叠实现（chatReasonBlock /
// chatReasonFoldedPeek）退役——「展开/折叠」语义已由「高度档」取代（design-ch4-frontend-theme §六「Think 块」）

// 注入报告 HTML——新会话前文加载明细（ok/missing/error 三态 + 字符数 + 注入工具组；history 首块渲染）
function chatInjectReportHtml(p) {
    var files = p.files || [];
    var total = p.total || 0;
    var ok = p.ok || 0;
    var missing = p.missing || 0;
    var failed = p.failed || 0;
    var html = '<div class="inject-report">'
        + '<div class="ir-head">📚 前文加载：' + ok + '/' + total + ' 成功'
        + (missing > 0 ? ' · 缺失 ' + missing : '')
        + (failed > 0 ? ' · 失败 ' + failed : '')
        + '</div>';
    if (files.length > 0) {
        html += '<div class="ir-list">';
        for (var i = 0; i < files.length; i++) {
            var f = files[i];
            var icon = '✅';
            var cls = 'ok';
            if (f.status === 'missing') { icon = '⚠️'; cls = 'missing'; }
            else if (f.status === 'error') { icon = '❌'; cls = 'error'; }
            html += '<div class="ir-item ' + cls + '">' + icon + ' ' + escapeHtml(f.file || '')
                + (f.status === 'ok' && f.chars > 0 ? '（' + f.chars + ' 字符）' : '')
                + (f.message ? ' — ' + escapeHtml(f.message) : '')
                + '</div>';
        }
        html += '</div>';
    }
    // Q5 注入工具组——独立气泡（组名 + 工具名列表；提示"新会话已加载工具"；desc 不进展示）
    var groups = p.toolGroups || [];
    if (groups.length > 0) {
        var toolCount = 0;
        for (var g = 0; g < groups.length; g++) { toolCount = toolCount + (groups[g].tools || []).length; }
        html += '<div class="ir-tools"><div class="ir-tools-head">🛠️ 已启用 ' + toolCount + ' 个工具</div>';
        for (var g2 = 0; g2 < groups.length; g2++) {
            var names = [];
            var tools = groups[g2].tools || [];
            for (var t = 0; t < tools.length; t++) { names.push(tools[t].name); }
            html += '<div class="ir-tool-row"><span class="ir-tool-group">' + escapeHtml(groups[g2].group || '内置') + '</span> —— ' + escapeHtml(names.join('、')) + '</div>';
        }
        html += '</div>';
    }
    html += '</div>';
    return html;
}

// 废弃块渲染——timeback 回收区间的合并归档（已被主干排除；折叠气泡，展开看全文）
// 语义（莎 2026-09-29）：这些内容不在前文面（LLM 看不见），仅人可见留档——独立块型，不进对话流、不参与 QQ 转发
function chatRenderVoid(p) {
    var wrap = chatBubble('assistant', 'void');
    var det = document.createElement('details');
    det.className = 'chat-void';
    var sum = document.createElement('summary');
    var n = (typeof p.count === 'number') ? p.count : 0;
    var vtxt = (typeof p.text === 'string') ? p.text : '';
    var vlines = (vtxt.length === 0) ? 0 : vtxt.split('\n').length;
    sum.textContent = '⏪ 已废弃 · ' + n + ' 条 · ' + vlines + ' 行 ' + chatFmtCount(vtxt.length) + ' 字符（timeback 回收 · 仅留档）';
    det.appendChild(sum);
    var body = document.createElement('div');
    body.className = 'chat-void-body';
    body.textContent = p.text || '';
    det.appendChild(body);
    wrap.appendChild(det);
}

function chatRenderHistory(data) {
    // F4 视图块历史渲染——按 blocks[] renderType 分派（view 协议；无 messages[] 旧结构）
    chatMsgs.textContent = '';
    var blocks = data.blocks || [];
    // 思考块一律折叠——与实时渲染一致（2026-09-17：思考结束即折叠，无最终回复展开特例）
    for (var i = 0; i < blocks.length; i++) {
        var blk = blocks[i];
        var p = blk.payload || {};
        if (blk.renderType === 'user') {
            var ub = chatBubble('user');
            // A65 图片包裹——历史重建与实时渲染同一出口（命中即缩略图组 + 正文）
            chatUserFill(ub, p.content || '');
        } else if (blk.renderType === 'reason') {
            var rb = chatBubble('assistant', 'reason');
            rb.appendChild(chatThinkBlock(p.content || '', null));
        } else if (blk.renderType === 'toolcard') {
            var tb = chatBubble('assistant', 'tool');
            tb.appendChild(chatToolCard(p));
        } else if (blk.renderType === 'retry') {
            // S2 §8.4——历史重建：重试过程记录气泡（retry 块随 view.json 落盘；A55 改走渲染单例）
            chatRenderRetry(p);
        } else if (blk.renderType === 'error') {
            // A55——历史重建：LLM 错误气泡（error 块随 view.json 落盘；与实时事件共用渲染单例）
            chatRenderError(p.text || 'LLM 错误');
        } else if (blk.renderType === 'inject_report') {
            // 注入报告——新会话前文加载明细（ok/missing/error 三态 + 字符数；独立持久化字段 Rebuild 不清）
            var rb2 = chatBubble('assistant', 'inject');
            rb2.innerHTML = chatInjectReportHtml(p);
        } else if (blk.renderType === 'roundsum') {
            // roundsum 轮末统计——历史重建：独立气泡（本轮 Token 消耗 + 工具次数 + 四态用时 + 总耗时）
            chatOnRoundSum(p);
        } else if (blk.renderType === 'void') {
            // 废弃块——timeback 回收区间的合并归档（已被主干排除，前端仅「能看」；不进对话流、不参与 QQ 转发）
            chatRenderVoid(p);
        } else if (blk.renderType === 'text') {
            // F3 MD 渲染——历史 text 块同样走解析器（与实时渲染一致）；md-block 包裹=CSS 作用域锚点
            // P6b 节点操作条——msgIndex 顶层字段（视图块携带真实前文顺序；roundsum/inject_report=-1 不挂）
            var cb = chatBubble('assistant');
            // A65——历史 text 块同走 chatMdFill（含包裹时先出缩略图组；无包裹与旧行为同构）
            chatMdFill(cb, p.content || '');
            chatAppendNodeActions(cb, blk.msgIndex);
        }
    }
    // P9.3 会话归属动态化——SSE sessionId 随会话 ID（时间戳）变化；history 先于任何 view 事件到达（loading→idle 时序保证）
    // P20-P3-7 归属修正——渲染层不写全局状态：sessionId 随返回值交调用方（chat-core.chatLoadHistory）落库
    var sid = data.sessionId || CHAT_SESSION;
    // A65 口径统一——「前文 n 条」= 送入 LLM 的消息数（ctxCount 实时值；旧数据缺字段回落视图块数）
    var ctxCount = (data.ctxCount !== undefined) ? data.ctxCount : (data.count || 0);
    var hs = data.stats;
    // 前文长度 = 最近一次请求的单次 prompt（context 字段）；旧数据无 context 时回退累计值
    var ctxTokens = hs ? ((hs.context !== undefined && hs.context > 0) ? hs.context : (hs.prompt || 0)) : 0;
    chatInfoSet(sid, ctxCount, ctxTokens);
    chatScrollBottom(true);
    return data.sessionId || '';
}

// P6b 节点操作条——text 块底部两按钮（⟲ 回滚 / ⧉ 分支）；指令走 command 总线（单向数据流：前端零寻路，只回传 MsgIndex）
function chatAppendNodeActions(bubble, msgIndex) {
    if (msgIndex === undefined || msgIndex === null || msgIndex < 0) { return; }
    var bar = document.createElement('div');
    bar.className = 'node-actions';
    var rb = document.createElement('button');
    rb.type = 'button';
    rb.className = 'node-btn node-btn-rollback';
    rb.title = '从此处继续对话（回滚——该回复后的内容将截断，不可恢复）';
    rb.textContent = '⟲';
    rb.addEventListener('click', function () {
        if (!window.confirm('从此处继续对话？该回复之后的所有消息将被截断（不可恢复）。')) { return; }
        fetch('/api/v1/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: 'session.rollback ' + msgIndex })
        }).catch(function (e) { uiWarn('回滚指令投递', e); });
        chatInfo.textContent = '回滚已投递——建议刷新浏览器页面';
    });
    var fb = document.createElement('button');
    fb.type = 'button';
    fb.className = 'node-btn node-btn-fork';
    fb.title = '从此处新建独立 Cat（以该回复为起点分支新实例，继承配置与前文）';
    fb.textContent = '⧉';
    fb.addEventListener('click', function () {
        var name = window.prompt('新 Cat 显示名：', 'fork-' + msgIndex);
        if (!name || name.length === 0) { return; }
        fetch('/api/v1/command', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: 'session.fork ' + name + ' ' + msgIndex })
        }).catch(function (e) { uiWarn('分支指令投递', e); });
        chatInfo.textContent = '分支指令已投递——请回到主控界面选择新会话';
    });
    bar.appendChild(rb);
    bar.appendChild(fb);
    bubble.appendChild(bar);
}

// 时长格式化——毫秒 → 可读（<60s → x.xs；≥60s → xm x.xs；≥3600s → xh xm x.xs——hour 最高单位不再进位；状态条/roundsum 共用）
// 2026-09-17：单位英文化（与状态条英文标签同语言——原「x分x.x秒 / x小时x分x.x秒」退役）
function chatFmtMs(ms) {
    var s = (ms || 0) / 1000;
    if (s >= 3600) {
        var hours = Math.floor(s / 3600);
        var rest = s - hours * 3600;
        var mins = Math.floor(rest / 60);
        var secs = rest - mins * 60;
        return hours + 'h' + mins + 'm' + secs.toFixed(1) + 's';
    }
    if (s >= 60) {
        var mins2 = Math.floor(s / 60);
        var secs2 = s - mins2 * 60;
        return mins2 + 'm' + secs2.toFixed(1) + 's';
    }
    return s.toFixed(1) + 's';
}

// roundsum 轮末统计气泡——本轮 Token 消耗 + 工具次数 + 请求次数 + 六态用时 + 总耗时（宿主 CloseRound 推送/历史重建渲染；弱化系统样式）
function chatOnRoundSum(payload) {
    var d = payload.data || {};
    var phases = d.phases || {};
    var miss = (d.miss !== undefined) ? d.miss : ((d.prompt || 0) - (d.cacheHit || 0));
    if (miss < 0) { miss = 0; }
    var b = chatBubble('assistant', 'roundsum');
    var html = '<div class="rs-head">📊 Round</div>';
    // cache 命中率——cacheHit / prompt（prompt=0 时 0%）
    var promptTotal = d.prompt || 0;
    var hitRate = (promptTotal > 0) ? ((d.cacheHit || 0) / promptTotal * 100) : 0;
    html += '<div class="rs-tok">↑' + chatFmtCount(promptTotal)
        + ' ↓' + chatFmtCount(d.completion || 0)
        + ' cache ' + chatFmtCount(d.cacheHit || 0)
        + ' miss ' + chatFmtCount(miss)
        + ' 🎯' + hitRate.toFixed(1) + '%</div>';
    // 第二行——工具次数 · 请求次数 · All 总耗时（2026-09-17 重排：All 自六态行挪入本行并列；三段各自着色）
    var toolsLine = '';
    if (d.toolCount > 0) { toolsLine = '<span class="rs-tool">🔧 Tool ' + d.toolCount + '</span>'; }
    if (d.requests !== undefined) {
        // A114 平均首 token 延迟——link（请求发出到首个语义增量帧）累计 ÷ 请求次数；旧载荷无 link 时不加后缀
        var apiText = '🔄 Api ' + (d.requests || 0);
        var linkMs = phases.link || 0;
        if (d.requests > 0 && linkMs > 0) {
            apiText += '（' + (linkMs / 1000 / d.requests).toFixed(2) + ' s /use）';
        }
        toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-api">' + apiText + '</span>';
    }
    toolsLine += (toolsLine ? ' · ' : '') + '<span class="rs-all">⏱ All ' + chatFmtMs(d.elapsedMs) + '</span>';
    html += '<div class="rs-tools">' + toolsLine + '</div>';
    // 六态用时（idle 不计时故不入载荷；非零态上尾巴 + All 总计）
    var phasesMeta = [
        { key: 'link', label: 'Link' },
        { key: 'wait', label: 'Wait' },
        { key: 'think', label: 'Think' },
        { key: 'tool', label: 'Tool' },
        { key: 'run', label: 'Run' },
        { key: 'reply', label: 'Reply' }
    ];
    // 第三行——六态用时（All 已挪至第二行；单色弱化——2026-09-17 试过分色，观感偏杂，回退全灰）
    var tparts = [];
    for (var pi = 0; pi < phasesMeta.length; pi++) {
        var pv = phases[phasesMeta[pi].key] || 0;
        if (pv > 0) { tparts.push(phasesMeta[pi].label + ' ' + chatFmtMs(pv)); }
    }
    html += '<div class="rs-times">' + tparts.join(' · ') + '</div>';
    b.innerHTML = html;
}

// ============ A65 对话图片附件——包裹解析与渲染（规格 Project/CH4/design-ch4-chat-images.md §四/§六） ============
// 严格门：段头 + ≥1 条目 + 段尾齐备，包裹内部无杂行；不成立 = 整段按普通文本（不渲染、不报错）
// 单一出口：标记 / 条目正则 / 路径→URL 只在本节定义——user 泡、assistant 泡、未来工具卡共用
var CHAT_IMG_OPEN = '[image-open]';
var CHAT_IMG_END = '[image-end]';
var CHAT_IMG_ITEM = /^图片(\d+)-(\d+)：(.+)$/;

function chatImgSplit(text) {
    // 包裹解析——命中 {items:[{ref,path}], body}；不成立 {items:[], body:原文}
    var raw = (text === undefined || text === null) ? '' : String(text);
    var lines = raw.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
    var i = 0;
    while (i < lines.length && lines[i].trim() === '') { i = i + 1; }
    if (i >= lines.length || lines[i].trim() !== CHAT_IMG_OPEN) { return { items: [], body: raw }; }
    i = i + 1;
    var items = [];
    while (i < lines.length) {
        var t = lines[i].trim();
        if (t === CHAT_IMG_END) { break; }
        if (t === '') { return { items: [], body: raw }; }
        var m = CHAT_IMG_ITEM.exec(t);
        if (!m) { return { items: [], body: raw }; }
        items.push({ ref: m[1] + '-' + m[2], path: m[3].trim() });
        i = i + 1;
    }
    if (items.length === 0 || i >= lines.length) { return { items: [], body: raw }; }
    i = i + 1;
    return { items: items, body: lines.slice(i).join('\n').replace(/^\s+/, '').replace(/\s+$/, '') };
}

function chatImgName(path) {
    // 路径末段——文件名（本地路径与 http(s) URL 通用；查询串剥离）
    var parts = String(path === undefined || path === null ? '' : path).split(/[\\/]/);
    var name = parts.length > 0 ? parts[parts.length - 1] : '';
    if (name.indexOf('?') >= 0) { name = name.split('?')[0]; }
    return name;
}

function chatImgUrl(path) {
    // 路径 → 可渲染 URL（单一出口）——四态：
    // ① http(s) 直通；② 受控根寻址 `<rootId>:<rel>` 整串进端点；③ 本地绝对路径整串进端点（宿主按受控根前缀匹配，
    //    不在根内则回落缓存目录文件名）；④ 裸文件名进缓存目录分支
    // （浏览器不认本地路径；端点按受控根 / 缓存目录白名单解析）
    var raw = String(path === undefined || path === null ? '' : path).trim();
    if (raw.length === 0) { return ''; }
    if (raw.indexOf('http://') === 0 || raw.indexOf('https://') === 0) { return raw; }
    if (chatImgIsRootAddress(raw) || chatImgIsAbsolutePath(raw)) {
        return '/api/v1/cache-image/' + chatImgRootUrl(raw);
    }
    var name = chatImgName(raw);
    if (name === '') { return ''; }
    return '/api/v1/cache-image/' + encodeURIComponent(name);
}

function chatImgIsAbsolutePath(path) {
    // 本地绝对路径判定——UNC `\\host\share` / 正斜杠根 `/x` / 盘符 `C:\` 或 `C:/`
    var raw = String(path === undefined || path === null ? '' : path);
    if (raw.length === 0) { return false; }
    if (raw.indexOf('\\\\') === 0) { return true; }
    if (raw.indexOf('/') === 0) { return true; }
    return /^[a-zA-Z]:[\\/]/.test(raw);
}

function chatImgIsRootAddress(path) {
    // 受控根寻址判定——首个冒号在首个分隔符之前（Windows 盘符 C:\ 不算）；与宿主 IsRootAddress 同判据
    var raw = String(path === undefined || path === null ? '' : path);
    var colon = raw.indexOf(':');
    if (colon <= 0) { return false; }
    var slash = raw.indexOf('/');
    var back = raw.indexOf('\\');
    var sep = -1;
    if (slash >= 0 && back >= 0) { sep = slash < back ? slash : back; }
    else if (slash >= 0) { sep = slash; }
    else { sep = back; }
    if (sep >= 0 && sep < colon) { return false; }
    if (colon === 1 && sep === 2) { return false; }
    return true;
}

function chatImgRootUrl(path) {
    // 受控根 URL 段——分隔符统一为 /；首段（含根 id 与冒号）原样，其余逐段编码（保 / 不被转义）
    var raw = String(path === undefined || path === null ? '' : path).trim().replace(/\\/g, '/');
    var parts = raw.split('/');
    var out = [];
    for (var i = 0; i < parts.length; i++) {
        if (i === 0) { out.push(parts[i]); } else { out.push(encodeURIComponent(parts[i])); }
    }
    return out.join('/');
}

function chatImgThumb(item) {
    // 缩略图——角标（包裹条目）/ 文件名（工具输入） + 点击新标签开原图；取图失败原位回落原始路径文本
    var label = item.ref ? ('图片' + item.ref) : chatImgName(item.path);
    var fig = document.createElement('figure');
    fig.className = 'chat-img';
    var url = chatImgUrl(item.path);
    var a = document.createElement('a');
    a.href = url;
    a.target = '_blank';
    a.rel = 'noopener';
    var img = document.createElement('img');
    img.src = url;
    img.alt = item.ref ? ('图片' + item.ref) : '输入图片';
    img.addEventListener('error', function () {
        fig.className = 'chat-img missing';
        fig.textContent = (label ? (label + '：') : '') + item.path + '（取图失败）';
    });
    a.appendChild(img);
    fig.appendChild(a);
    if (label) {
        var cap = document.createElement('figcaption');
        cap.textContent = label;
        fig.appendChild(cap);
    }
    return fig;
}

function chatImageGroupFromPaths(paths) {
    // 工具输入图片组——路径列表 → 缩略图组（无编号：非包裹条目，标题走文件名）
    var items = [];
    for (var i = 0; i < paths.length; i++) { items.push({ ref: '', path: paths[i] }); }
    return chatImageGroup(items);
}

function chatImageGroup(items) {
    // 缩略图组——包裹命中时的图片块（各泡共用）
    var wrap = document.createElement('div');
    wrap.className = 'chat-imgs';
    for (var i = 0; i < items.length; i++) {
        wrap.appendChild(chatImgThumb(items[i]));
    }
    return wrap;
}

function chatUserFill(bubble, content) {
    // user 泡内容填充——无包裹走原路径（textContent，零回归）；有包裹 = 缩略图组 + 正文文本
    var r = chatImgSplit(content);
    if (r.items.length === 0) {
        bubble.textContent = content;
        return;
    }
    bubble.appendChild(chatImageGroup(r.items));
    if (r.body.length > 0) {
        var t = document.createElement('div');
        t.className = 'chat-user-text';
        t.textContent = r.body;
        bubble.appendChild(t);
    }
}

// A81 MD 块复制原文（2026-09-23）——代码块 / 表格右上角图标按钮
// 分层：chat-md.js 出结构（.md-copy 包裹 + data-md 源文本）→ 本层插按钮与绑定事件
// 源文本单一出口：data-md 优先（表格——含分隔行原文）；否则取代码块正文（pre code 的 textContent 天然等于原文）
function chatMdCopySource(wrap) {
    var src = wrap.getAttribute('data-md');
    if (src !== null && src !== undefined) { return src; }
    var code = wrap.querySelector('pre code');
    if (code) { return code.textContent; }
    return '';
}

// 剪贴板写入——navigator.clipboard 单一通道（127.0.0.1 / localhost 属安全上下文）；不可用时拒绝（调用侧告警）
function chatWriteClipboard(text) {
    if (typeof navigator === 'undefined' || !navigator.clipboard || typeof navigator.clipboard.writeText !== 'function') {
        return Promise.reject(new Error('clipboard 不可用'));
    }
    return navigator.clipboard.writeText(text);
}

// 线条复制图标——双框（Feather 风格内联 SVG：零依赖、零字体差异、随 currentColor 取灰）
function chatMdCopyIcon() {
    var ns = 'http://www.w3.org/2000/svg';
    var svg = document.createElementNS(ns, 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', '12');
    svg.setAttribute('height', '12');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    var back = document.createElementNS(ns, 'rect');
    back.setAttribute('x', '9');
    back.setAttribute('y', '9');
    back.setAttribute('width', '13');
    back.setAttribute('height', '13');
    back.setAttribute('rx', '2');
    var front = document.createElementNS(ns, 'path');
    front.setAttribute('d', 'M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1');
    svg.appendChild(back);
    svg.appendChild(front);
    return svg;
}

function chatAppendMdCopyBtn(wrap) {
    // 复制按钮——线条图标（内联 SVG；零文本节点：不污染 textContent 断言与朗读）；成功切 done 类 1.2s 后复原
    var btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'md-copy-btn';
    btn.title = '复制原文';
    btn.setAttribute('aria-label', '复制原文');
    btn.appendChild(chatMdCopyIcon());
    btn.addEventListener('click', function () {
        chatWriteClipboard(chatMdCopySource(wrap)).then(function () {
            btn.classList.add('done');
            window.setTimeout(function () { btn.classList.remove('done'); }, 1200);
        }).catch(function (e) { uiWarn('复制原文', e); });
    });
    wrap.insertBefore(btn, wrap.firstChild);
}

function chatBindMdCopy(root) {
    var wraps = root.querySelectorAll('.md-copy');
    for (var i = 0; i < wraps.length; i = i + 1) {
        chatAppendMdCopyBtn(wraps[i]);
    }
}

function chatMdFill(bubble, content) {
    // assistant 泡内容填充——无包裹走原路径（md-block 单块）；有包裹 = 缩略图组 + MD 正文块
    var r = chatImgSplit(content);
    var text = (r.items.length > 0) ? r.body : content;
    if (r.items.length > 0) {
        bubble.appendChild(chatImageGroup(r.items));
    }
    var md = document.createElement('div');
    md.className = 'md-block';
    md.innerHTML = mdToHtml(text);
    chatBindMdCopy(md);   // A81——代码块 / 表格挂复制按钮（整块渲染后一次性挂载）
    bubble.appendChild(md);
}
