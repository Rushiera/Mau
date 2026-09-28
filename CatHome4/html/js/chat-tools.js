// CH4 外观层——chat-tools.js：工具块渲染（骨架层 + 逐工具填充单例 + 回落）
// 定位：工具卡内部渲染的唯一实现——规格 Project/CH4/design-ch4-frontend-tools.md
// 三层：骨架（语义大类 → 壳）· 填充（工具一对一 → 肉）· 回落（调用方兜底，保持现行渲染）
// 纪律：纯函数、无状态、无网络、无 innerHTML（文本一律 textContent）；未登记工具返回 null → 调用方走现行渲染（零回归）
// 加载顺序：chat-md.js → chat-cmd.js → chat-tools.js → chat-view.js → chat-core.js（chat.html 引导层）

// ═══════════════════════════════════════════
// 阈值与共用小件
// ═══════════════════════════════════════════

var CHAT_SEG_FOLD_LINES = 5;      // 段折叠阈值——行数 ≤5 默认展开；>5 折叠为「前 2 + 摘要行 + 后 2」
var CHAT_SEG_PEEK_HEAD = 2;       // 折叠摘要——保留的首行数
var CHAT_SEG_PEEK_TAIL = 2;       // 折叠摘要——保留的末行数

// 规模描述——「1.50k 字符 / 42 行」（大数走 chatFmtCount，与折叠行后缀同源）
function chatSegSize(text) {
    var t = text || '';
    var lines = (t.length === 0) ? 0 : t.split('\n').length;
    return chatFmtCount(t.length) + ' 字符 / ' + lines + ' 行';
}

// 段落——details.seg（二级折叠：点折叠头展开全量；限高滚动由 CSS 承担）
// 折叠判据（2026-09-18）：内容行数 ≤5 → 默认展开（折叠态与展开态内容一致，无需折叠）
//                        >5 → 默认折叠，折叠态即「5 行摘要」（首 2 + 提示行 + 末 2）
// 🔴 摘要必须挂在 summary 内——details 折叠时非 summary 子元素被浏览器隐藏（peek 放 body 会不可见）
function chatSeg(cls, cap, fill, peek) {
    var det = document.createElement('details');
    det.className = 'seg ' + cls;
    var sum = document.createElement('summary');
    sum.className = 'seg-cap';
    sum.appendChild(document.createTextNode(cap));
    var peekText = chatSegPeekText(peek);
    if (peekText.length > 0) {
        var pk = document.createElement('span');
        pk.className = 'seg-peek';
        pk.textContent = peekText;
        sum.appendChild(pk);
    } else {
        det.open = true;
    }
    det.appendChild(sum);
    var body = document.createElement('div');
    body.className = 'seg-body';
    fill(body);
    det.appendChild(body);
    return det;
}

// 段折叠摘要——行数 ≤5 返回空串（不折叠）；否则「首 2 行 + 折叠提示 + 末 2 行」
// 提示行口径——被折叠的中间行数 + 其字符数（不含首尾保留行）
function chatSegPeekText(text) {
    if (typeof text !== 'string' || text.length === 0) { return ''; }
    var lines = text.split('\n');
    if (lines.length <= CHAT_SEG_FOLD_LINES) { return ''; }
    var head = lines.slice(0, CHAT_SEG_PEEK_HEAD);
    var tail = lines.slice(lines.length - CHAT_SEG_PEEK_TAIL);
    var mid = lines.slice(CHAT_SEG_PEEK_HEAD, lines.length - CHAT_SEG_PEEK_TAIL);
    return head.join('\n') + '\n… 折叠了 ' + mid.length + ' 行 ' + mid.join('\n').length + ' 字符 …\n' + tail.join('\n');
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
// 工具填充单例——每工具一支（覆盖表声明式：图标 / 标签 / 输入行 / 输出行 / 徽标；Z8 起折叠行唯一产出在前端）
// 返回 {tag, tagCls, segs}：tag = 折叠行工具变体标签（可空）；segs = 段数组
// ═══════════════════════════════════════════

// 填充层——逐工具「肉」归 CHAT_TOOL_OVERRIDES 覆盖表（见文件末）；此处不再设填充函数注册表

// ═══════════════════════════════════════════
// 分派入口——chat-view.js chatToolCard 消费
// ═══════════════════════════════════════════

// 工具块内部渲染——三级分派（规格 §四）：骨架落位表（壳）→ 骨架通用渲染 → 形态探测回落
// 逐工具「肉」由 CHAT_TOOL_OVERRIDES 覆盖表在骨架内部逐字段合并（无需前置分支）
// tool = toolcard 载荷（name / arguments / result / summary / toolIndex / toolTotal）
// 返回 {tag, tagCls, segs}——永不返回 null（未登记工具走探测骨架，禁止空白、禁止静默）
function chatToolBody(tool) {
    if (!tool || typeof tool.name !== 'string' || tool.name.length === 0) { return null; }
    var skel = CHAT_TOOL_SKELETONS[tool.name];
    if (typeof skel === 'string') {
        var fn = CHAT_SKEL_RENDERERS[skel];
        if (typeof fn === 'function') { return fn(tool); }
    }
    return chatSkelProbe(tool);
}

// ═══════════════════════════════════════════
// 骨架层——语义大类 → 壳（规格 §二 骨架清单 / §三·一 落位总表）
// 分派序：填充注册表（肉）→ 骨架落位表（壳）→ 形态探测回落（规格 §四 三级）
// 纪律：骨架只决定分区结构与头要素，不猜工具语义；解析不出计数就不标计数，不改写原文
// ═══════════════════════════════════════════

// 骨架落位表——工具名 → 骨架 id（未登记骨架的工具走形态探测：{ 可解析 → json，否则 text）
var CHAT_TOOL_SKELETONS = {
    // exec——进程 / 命令执行
    'powershell': 'exec',
    'powershell7': 'exec',
    'mau-setup': 'exec',
    'host-reload': 'exec',
    'majordomo-restart': 'exec',
    'majordomo-cmd': 'exec',
    'temp-exec': 'exec',
    // diagnostics——诊断列表
    'cs-check': 'diagnostics',
    'cs-build': 'diagnostics',
    'cs-comment_check': 'diagnostics',
    'cs-dead': 'diagnostics',
    'cs-format': 'diagnostics',
    'mau-verify': 'diagnostics',
    'mau-gen': 'diagnostics',
    'mau-proj': 'diagnostics',
    // listing——列举
    'file-tree': 'listing',
    'file-find': 'listing',
    'file-version': 'listing',
    'cs-list': 'listing',
    'config-list': 'listing',
    // matches——检索命中
    'text-grep': 'matches',
    'cs-find_ref': 'matches',
    // lines——带行号文本
    'text-read_lines': 'lines',
    'cs-read': 'lines',
    // file——文件内容
    'text-read': 'file',
    'text-read_between': 'file',
    // json——通用结构
    'config-get': 'json',
    'host-flows': 'json',
    'temp-info': 'json',
    // text——纯文本兜底
    'text-write': 'text',
    'text-append': 'text',
    'text-replace': 'text',
    'file-move': 'text',
    'file-delete': 'text',
    'file-copy': 'text',
    'cs-patch': 'text',
    'cs-member': 'text',
    'cs-comment': 'text',
    'config-set': 'text',
    'config-reset': 'text',
    'config-cat-get': 'text',
    'config-cat-set': 'text',
    'web-search': 'text',
    'image-analyze': 'text',
    'Note': 'text',
    'time': 'text',
    'random': 'text',
    'sleep': 'text',
    'timer': 'text',
    'info': 'info',
    'majordomo-catinfo': 'catinfo',
    'pack': 'text'
};

// ── 共用小件：参数与结果 ──

// 参数对象——arguments JSON → 对象（解析失败 / 非对象 → 空对象，不抛）
function chatArgObj(tool) {
    var o = chatTryJson(tool ? tool.arguments : null);
    if (!o) { return {}; }
    return o;
}

// 参数键值对——[{k, v}]；值单行化（摘要行不折行的同源处理）
function chatArgPairs(tool) {
    var o = chatArgObj(tool);
    var keys = Object.keys(o);
    var out = [];
    for (var i = 0; i < keys.length; i++) {
        var v = o[keys[i]];
        if (typeof v !== 'string') { v = JSON.stringify(v); }
        out.push({ k: keys[i], v: String(v).replace(/\r/g, '').replace(/\n/g, ' ') });
    }
    return out;
}

// 键值行块——逐行等宽（键走键色、值走值色；长值由 CSS 折行，不截断）
function chatSegKv(body, pairs) {
    for (var i = 0; i < pairs.length; i++) {
        var row = document.createElement('div');
        row.className = 'seg-kv';
        var k = document.createElement('span');
        k.className = 'seg-kv-k';
        k.textContent = pairs[i].k + ': ';
        var v = document.createElement('span');
        v.className = 'seg-kv-v';
        v.textContent = pairs[i].v;
        row.appendChild(k);
        row.appendChild(v);
        body.appendChild(row);
    }
}

// 输入段——填充层声明 inputLines 时用自然语言意图行；否则通用键值表
function chatSegInput(tool, cap) {
    var ov = chatToolOverride(tool.name);
    var lines = null;
    if (ov && typeof ov.inputLines === 'function') {
        lines = ov.inputLines(chatArgObj(tool)) || [];
    }
    var pairs = lines ? [] : chatArgPairs(tool);
    var peekLines = [];
    var head = cap;
    if (lines) {
        for (var i = 0; i < lines.length; i++) { peekLines.push(lines[i]); }
        if (!head || head.length === 0) { head = '输入 · ' + lines.length + ' 行'; }
    } else {
        for (var j = 0; j < pairs.length; j++) { peekLines.push(pairs[j].k + ': ' + pairs[j].v); }
        if (!head || head.length === 0) { head = '输入' + (pairs.length > 0 ? ' · ' + pairs.length + ' 项' : ''); }
    }
    return chatSeg('seg-in', head, function (body) {
        if (lines) {
            if (lines.length === 0) { chatSegBlock(body, 'ta', '（无参数）'); return; }
            chatSegLines(body, lines, 'seg-line');
            return;
        }
        if (pairs.length === 0) { chatSegBlock(body, 'ta', '（无参数）'); return; }
        chatSegKv(body, pairs);
    }, peekLines.join('\n'));
}

// 输入图片段——覆盖表声明 inputImages 的工具：在输入段之前展示被处理的图片（A65 §六 · 工具卡渲染）
// 前端零路径知识：本地路径交 chatImgUrl 单一出口转取图端点；http(s) 直通
// 未声明 / 返回空 → null（调用方跳过，零回归）
function chatSegImages(tool) {
    var ov = chatToolOverride(tool.name);
    if (!ov || typeof ov.inputImages !== 'function') { return null; }
    if (typeof chatImageGroupFromPaths !== 'function') { return null; }
    var paths = ov.inputImages(chatArgObj(tool)) || [];
    if (paths.length === 0) { return null; }
    return chatSeg('seg-img', '输入图片 · ' + paths.length + ' 张', function (body) {
        body.appendChild(chatImageGroupFromPaths(paths));
    }, paths.join('\n'));
}

// 结果文本行——空串 / undefined → 空数组（保持原行内容，不 trim——缩进即语义）
function chatLines(text) {
    if (typeof text !== 'string' || text.length === 0) { return []; }
    return text.split('\n');
}

// 非空行数——空白行不计
function chatCountLines(lines) {
    var n = 0;
    for (var i = 0; i < lines.length; i++) {
        if (lines[i].replace(/\s/g, '').length > 0) { n = n + 1; }
    }
    return n;
}

// 结果失败态——ERR / ROLLED_BACK / FAIL 前缀，或结构化返回头 ok:false（规格 §七；单一出口，各骨架不再自加后缀）
function chatIsErrResult(text) {
    if (typeof text !== 'string' || text.length === 0) { return false; }
    if (text.indexOf('ERR') === 0 || text.indexOf('ROLLED_BACK') === 0 || text.indexOf('FAIL') === 0) { return true; }
    var head = chatMetaHead(text);
    return !!(head && head.meta.ok === false);
}

// 输出段通用外壳——处理中 / 空输出 / 失败三态统一（规格 §七 回落矩阵）
// 覆盖优先：填充层声明 outputLines（逐工具自然语言化）→ 替代骨架通用渲染；badge → 覆盖折叠头徽标
// peek——折叠摘要用文本（默认取结果原文；多块结果由骨架传入合并文本）
function chatSegOutput(tool, cap, render, peek) {
    var ov = chatToolOverride(tool.name);
    var text = tool.result;
    var head = cap || '输出';
    if (ov && typeof ov.badge === 'function' && typeof text === 'string' && text.length > 0) {
        var extra = ov.badge(text, chatArgObj(tool));
        if (typeof extra === 'string' && extra.length > 0) { head = '输出' + extra; }
    }
    if (text === undefined) { head = head + ' · 处理中'; }
    else if (text === '') { head = head + ' · 无输出'; }
    else if (chatIsErrResult(text)) { head = head + ' · 失败'; }
    var peekText = (typeof peek === 'string') ? peek : text;
    var draw = render;
    if (ov && typeof ov.outputLines === 'function') {
        draw = function (body, t, isErr) {
            var ls = ov.outputLines(t, chatArgObj(tool), isErr);
            if (!ls) { render(body, t, isErr); return; }
            chatSegLines(body, ls, isErr ? 'seg-line err' : 'seg-line');
        };
    }
    return chatSeg('seg-out', head, function (body) {
        if (text === undefined) { chatSegBlock(body, 'ta ' + CHAT_PENDING_HOLD_CLS, chatPendingHoldText(0)); return; }
        if (text === '') { chatSegBlock(body, 'tr', '（无输出）'); return; }
        draw(body, text, chatIsErrResult(text));
    }, peekText);
}

// 行列表块——逐行独立 div（等宽 + 缩进保留；便于长列表滚动定位）
function chatSegLines(body, lines, cls) {
    for (var i = 0; i < lines.length; i++) {
        chatSegBlock(body, cls, lines[i]);
    }
}

// ═══════════════════════════════════════════
// 骨架实现——每类一支通用渲染（壳）
// 返回 {tag, tagCls, segs} 与填充单例同构（调用方零分支）
// ═══════════════════════════════════════════

// ── exec：进程 / 命令执行（输入：意图 + 原文；输出：exit 徽标 + stdout / stderr 分段 + 截断）──
// opts.tag / opts.tagCls——工具变体标签（如 PowerShell PS 5.1 / PS 7）；opts 省略即无标签
function chatSkelExec(tool, opts) {
    var o = opts || {};
    var ov = chatToolOverride(tool.name);
    var args = chatArgObj(tool);
    var cmd = chatField(args, 'command');
    var cwd = chatField(args, 'cwd');
    var segs = [];

    // [段1] 输入——命令原文（cwd 前置一行）；无 command 键的工具（host-reload / mau-setup / temp-exec）走通用键值表
    if (cmd.length > 0) {
        var inParts = [];
        if (cwd.length > 0) { inParts.push('cwd: ' + cwd); }
        inParts.push(cmd);
        var inText = inParts.join('\n');
        var inCap = '输入 · 命令 ' + cmd.length + ' 字符' + (cwd.length > 0 ? ' · 指定 cwd' : '');
        segs.push(chatSeg('seg-in', inCap, function (body) {
            chatSegBlock(body, 'ta', inText);
        }, inText));
    } else {
        segs.push(chatSegInput(tool, ''));
    }

    // [段2] 输出——exit 徽标 + stdout / stderr 分段（各自规模）+ 截断 / 超时 / 空输出显式
    // 结构化返回优先（首行 JSON 元数据 + 正文定界行）——剥头后按正文渲染；无头结果原路 JSON 解析
    var head = chatMetaHead(tool.result);
    var r = head ? null : chatTryJson(tool.result);
    var bodyText = head ? head.body : tool.result;
    var outCap = '输出';
    var warnText = '';
    var blocks = [];
    if (tool.result === undefined) {
        outCap = '输出 · 处理中';
        blocks.push({ cap: '', cls: 'ta ' + CHAT_PENDING_HOLD_CLS, text: chatPendingHoldText(0) });
    } else if (head && typeof head.meta.stdoutLines === 'number') {
        // 结构化 ps 回执（首行头 + 正文）——正文按 stdoutLines / stderrLines 切分（不依赖内容分隔符，零撞车）
        var allLines = chatLines(bodyText);
        var soCount = head.meta.stdoutLines || 0;
        var seCount = head.meta.stderrLines || 0;
        var out = (soCount > 0) ? allLines.slice(0, soCount).join('\n') : '';
        var err = (seCount > 0) ? allLines.slice(soCount, soCount + seCount).join('\n') : '';
        outCap = '输出 · exit ' + head.meta.exit;
        if (out.length > 0) { blocks.push({ cap: 'stdout · ' + chatSegSize(out), cls: 'tr', text: out }); }
        if (err.length > 0) { blocks.push({ cap: 'stderr · ' + chatSegSize(err), cls: 'tr err', text: err }); }
        if (out.length === 0 && err.length === 0) { outCap = outCap + ' · 无输出'; }
        if (head.meta.truncated === true) {
            outCap = outCap + ' · ⚠️ 已截断';
            warnText = '⚠️ 输出已达上限被截断——后续内容未回传' + (head.meta.timeout === true ? '；进程超时已终止' : '');
        }
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
    } else if (tool.result === '') {
        outCap = '输出 · 无输出';
    } else {
        outCap = '输出 · 原始输出';
        blocks.push({ cap: '', cls: (chatIsErrResult(tool.result) ? 'tr err' : 'tr'), text: bodyText });
    }
    // 折叠摘要文本——stdout / stderr 合并（多块结果取整体）
    var outPeek = bodyText;
    if (r && typeof r.exit !== 'undefined') {
        outPeek = chatField(r, 'stdout') + '\n' + chatField(r, 'stderr');
    }
    segs.push(chatSeg('seg-out', outCap, function (body) {
        if (warnText.length > 0) { chatSegBlock(body, 'ta warn', warnText); }
        if (ov && typeof ov.outputLines === 'function' && typeof tool.result === 'string' && tool.result.length > 0) {
            chatSegLines(body, ov.outputLines(tool.result, args, chatIsErrResult(tool.result)) || [], 'seg-line');
            return;
        }
        if (blocks.length === 0) { chatSegBlock(body, 'tr', '（无输出）'); return; }
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
    }, outPeek));

    var tag = o.tag || ((ov && typeof ov.tag === 'string') ? ov.tag : '');
    var tagCls = o.tagCls || ((ov && typeof ov.tagCls === 'string') ? ov.tagCls : '');
    return { tag: tag, tagCls: tagCls, segs: segs };
}

// ── diagnostics：诊断列表（输入：目标；输出：计数徽标 + 诊断行列表）──
// 计数来源——JSON 结果取 errors / warnings；纯文本清单退化为行数徽标（解析不出就不标，不猜）
function chatDiagCounts(text) {
    var o = chatTryJson(text);
    if (!o) { return null; }
    var e = (typeof o.errors === 'number') ? o.errors : -1;
    var w = (typeof o.warnings === 'number') ? o.warnings : -1;
    if (e < 0 && w < 0) { return null; }
    return { errors: (e < 0 ? 0 : e), warnings: (w < 0 ? 0 : w) };
}

function chatSkelDiagnostics(tool) {
    var segs = [chatSegInput(tool, '')];
    // 结构化返回优先（首行 JSON 元数据 + 正文定界行）；旧格式原路解析（chatDiagCounts 认 JSON 结果）
    var head = chatMetaHead(tool.result);
    var bodyText = head ? head.body : tool.result;
    var counts = head ? chatDiagCountsFromMeta(head.meta) : chatDiagCounts(tool.result);
    var lines = chatLines(bodyText);
    var cap = '输出';
    if (counts) {
        cap = '输出 · ' + counts.errors + ' 错 ' + counts.warnings + ' 警';
    } else if (lines.length > 0) {
        cap = '输出 · ' + chatCountLines(lines) + ' 行';
    }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        if (lines.length === 0) { chatSegBlock(body, 'tr', '（无诊断输出）'); return; }
        chatSegLines(body, lines, isErr ? 'tr err' : 'tr');
    }, bodyText));
    return { tag: '', tagCls: '', segs: segs };
}

// ── listing：列举（输入：条件；输出：计数 + 条目列表，缩进保留）──
// 计数不含提示行（[git] / [skip] / [截断]）——提示是元信息，不是条目
function chatSkelListing(tool) {
    var segs = [chatSegInput(tool, '')];
    // 结构化返回优先（首行 JSON 元数据 + 正文定界行）；旧格式原路（纯文本清单）
    var head = chatMetaHead(tool.result);
    var bodyText = head ? head.body : tool.result;
    var lines = chatLines(bodyText);
    var items = [];
    for (var i = 0; i < lines.length; i++) {
        if (lines[i].replace(/\s/g, '').length === 0) { continue; }
        if (lines[i].indexOf('[git]') === 0 || lines[i].indexOf('[skip]') === 0 || lines[i].indexOf('[截断]') === 0) { continue; }
        items.push(lines[i]);
    }
    var cap = '输出';
    if (typeof bodyText === 'string' && bodyText.length > 0) { cap = '输出 · ' + items.length + ' 条目' + chatOvTotalTail(bodyText); }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        if (items.length === 0) { chatSegBlock(body, 'tr', '（无匹配条目）'); return; }
        chatSegLines(body, items, isErr ? 'tr err' : 'seg-line');
    }, bodyText));
    return { tag: '', tagCls: '', segs: segs };
}

// ── matches：检索命中（输入：检索条件；输出：路径 / 行号 / 上下文 三列）──
// 行形态 `[跨程序集] 路径:行:列: 上下文`（列号可缺；切不出行号的整行作路径，不丢行）
function chatParseHits(lines) {
    var hits = [];
    for (var i = 0; i < lines.length; i++) {
        var ln = lines[i];
        if (ln.replace(/\s/g, '').length === 0) { continue; }
        var cross = false;
        if (ln.indexOf('[跨程序集]') === 0) {
            cross = true;
            ln = ln.substring(10).replace(/^\s+/, '');
        }
        var m = /^(.+?):(\d+):(\d+):(.*)$/.exec(ln);
        if (m) {
            hits.push({ path: m[1], line: m[2], col: m[3], ctx: m[4], cross: cross });
            continue;
        }
        var m2 = /^(.+?):(\d+):(.*)$/.exec(ln);
        if (m2) {
            hits.push({ path: m2[1], line: m2[2], col: '', ctx: m2[3], cross: cross });
            continue;
        }
        hits.push({ path: ln, line: '', col: '', ctx: '', cross: cross });
    }
    return hits;
}

function chatSkelMatches(tool) {
    var segs = [chatSegInput(tool, '')];
    // 结构化返回优先（首行 JSON 元数据 + 正文定界行）
    var head = chatMetaHead(tool.result);
    var bodyText = head ? head.body : tool.result;
    var hits = chatParseHits(chatLines(bodyText));
    var cap = '输出';
    if (typeof bodyText === 'string' && bodyText.length > 0) { cap = '输出 · ' + hits.length + ' 命中'; }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        if (hits.length === 0) { chatSegBlock(body, 'tr', '（无命中）'); return; }
        for (var i = 0; i < hits.length; i++) {
            var row = document.createElement('div');
            row.className = 'seg-hit' + (isErr ? ' err' : '');
            if (hits[i].cross) {
                var cr = document.createElement('span');
                cr.className = 'seg-hit-cross';
                cr.textContent = '[跨程序集] ';
                row.appendChild(cr);
            }
            var p = document.createElement('span');
            p.className = 'seg-hit-path';
            p.textContent = hits[i].path;
            row.appendChild(p);
            if (hits[i].line.length > 0) {
                var l = document.createElement('span');
                l.className = 'seg-hit-line';
                l.textContent = (hits[i].col.length > 0) ? (':' + hits[i].line + ':' + hits[i].col) : (':' + hits[i].line);
                row.appendChild(l);
            }
            if (hits[i].ctx.length > 0) {
                var c = document.createElement('span');
                c.className = 'seg-hit-ctx';
                c.textContent = ':' + hits[i].ctx;
                row.appendChild(c);
            }
            body.appendChild(row);
        }
    }, bodyText));
    return { tag: '', tagCls: '', segs: segs };
}

// ── lines：带行号文本（输入：文件 + 区间 / 成员；输出：行号列 + 内容）──
// 行形态 `行号: 内容`（text-read_lines / cs-read 统一文件坐标系）；无行号前缀的行整行渲染
function chatSkelLines(tool) {
    var segs = [chatSegInput(tool, '')];
    // 结构化返回优先——meta.start 驱动行号列（cs-read 行尾标注剥离显示）；旧格式按行首 `N: ` 解析
    var head = chatMetaHead(tool.result);
    var bodyText = head ? head.body : tool.result;
    var lines = chatLines(bodyText);
    var startNo = (head && typeof head.meta.start === 'number') ? head.meta.start : -1;
    var cap = '输出';
    if (typeof bodyText === 'string' && bodyText.length > 0) { cap = '输出 · ' + chatCountLines(lines) + ' 行'; }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        if (lines.length === 0) { chatSegBlock(body, 'tr', '（无输出）'); return; }
        for (var i = 0; i < lines.length; i++) {
            var row = document.createElement('div');
            row.className = 'seg-line' + (isErr ? ' err' : '');
            if (startNo > 0) {
                // 结构化行号——start + 行序（后端已给准确区间，无需解析行尾 / 行首标注）
                var no = document.createElement('span');
                no.className = 'seg-line-no';
                no.textContent = String(startNo + i);
                var tx = document.createElement('span');
                tx.className = 'seg-line-tx';
                tx.textContent = chatStripLineMark(lines[i]);
                row.appendChild(no);
                row.appendChild(tx);
            } else {
                var m = /^(\d+): ?(.*)$/.exec(lines[i]);
                if (m) {
                    var no2 = document.createElement('span');
                    no2.className = 'seg-line-no';
                    no2.textContent = m[1];
                    var tx2 = document.createElement('span');
                    tx2.className = 'seg-line-tx';
                    tx2.textContent = m[2];
                    row.appendChild(no2);
                    row.appendChild(tx2);
                } else {
                    row.textContent = lines[i];
                }
            }
            body.appendChild(row);
        }
    }, bodyText));
    return { tag: '', tagCls: '', segs: segs };
}

// ── file / text：正文渲染（共用一支）──
// withSize=true（file）标规模「字符 / 行」；false（text）只标行数——纯文本兜底不给体量噪音
function chatSkelPlain(tool, withSize) {
    var segs = [];
    // A65——被处理的图片先行（覆盖表声明 inputImages 时；未声明零回归）
    var imgSeg = chatSegImages(tool);
    if (imgSeg) { segs.push(imgSeg); }
    segs.push(chatSegInput(tool, ''));
    // 结构化返回优先（首行 JSON 元数据 + 正文定界行）——剥头后按正文渲染
    var head = chatMetaHead(tool.result);
    var bodyText = head ? head.body : tool.result;
    var cap = '输出';
    if (typeof bodyText === 'string' && bodyText.length > 0) {
        if (withSize) {
            cap = '输出 · ' + chatSegSize(bodyText);
        } else {
            cap = '输出 · ' + chatCountLines(chatLines(bodyText)) + ' 行';
        }
    }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        chatSegBlock(body, isErr ? 'tr err' : 'tr', bodyText);
    }, bodyText));
    return { tag: '', tagCls: '', segs: segs };
}

// ── json：通用结构（输入：键值表；输出：顶层键值 / 原文回落）──
// 解析失败不静默——原样可见（规格 §七）；结构化返回时元数据头作键值表、正文另起文本块
function chatSkelJson(tool) {
    var segs = [chatSegInput(tool, '')];
    var head = chatMetaHead(tool.result);
    var parsed = head ? head.meta : chatTryJson(tool.result);
    var bodyText = head ? head.body : '';
    var pairs = [];
    if (parsed) {
        var keys = Object.keys(parsed);
        for (var i = 0; i < keys.length; i++) {
            var v = parsed[keys[i]];
            if (typeof v !== 'string') { v = JSON.stringify(v); }
            pairs.push({ k: keys[i], v: String(v) });
        }
    }
    var hasBody = (head !== null) && (bodyText.length > 0);
    var cap = '输出';
    if (parsed) { cap = '输出 · ' + pairs.length + ' 键' + (hasBody ? ' + 正文' : ''); }
    else if (tool.result !== undefined && tool.result !== '') { cap = '输出 · 原始输出'; }
    segs.push(chatSegOutput(tool, cap, function (body, text, isErr) {
        if (parsed) {
            chatSegKv(body, pairs);
            if (hasBody) { chatSegBlock(body, 'tr', bodyText); }
            return;
        }
        chatSegBlock(body, isErr ? 'tr err' : 'tr', text);
    }, hasBody ? bodyText : tool.result));
    return { tag: '', tagCls: '', segs: segs };
}

// ── info：本会话环境自省（返回体 = 分类 JSON 块；按大类渲染为键值表）──
// 口径（莎定 2026-09-18）：info 以 LLM 可读为第一目的（后端分类 JSON 块 / 缩进 / 中文直显）；
// 前端在此把各大类摊平成键值行——值内多字段用「 · 」连接，长值由 CSS 折行（不截断）
function chatSkelInfo(tool) {
    var segs = [chatSegInput(tool, '')];
    var d = chatTryJson(tool.result);
    if (!d) {
        // 非分类 JSON（provider 未注入 / 异常）——原文兜底，失败可见（不静默空白）
        segs.push(chatSegOutput(tool, '输出', function (body, text, isErr) {
            chatSegBlock(body, isErr ? 'tr err' : 'tr', text);
        }, tool.result));
        return { tag: '', tagCls: '', segs: segs };
    }
    var pairs = chatInfoPairs(d);
    segs.push(chatSegOutput(tool, '输出 · ' + pairs.length + ' 项', function (body, text, isErr) {
        chatSegKv(body, pairs);
    }, chatInfoPeek(pairs)));
    return { tag: '', tagCls: '', segs: segs };
}

// ── catinfo：全猫状态统计（返回体 = 整块分类 JSON；每猫一行键值）──
// 口径（莎定 2026-09-24）：与 info 同规格——后端整块分类 JSON（缩进 / 中文直显），前端摊平为键值行；
// 缺字段不显示该行（后端「无则省略键」约定——前端不补空行、不写 NaN）
function chatSkelCatInfo(tool) {
    var segs = [chatSegInput(tool, '')];
    var d = chatTryJson(tool.result);
    if (!d) {
        segs.push(chatSegOutput(tool, '输出', function (body, text, isErr) {
            chatSegBlock(body, isErr ? 'tr err' : 'tr', text);
        }, tool.result));
        return { tag: '', tagCls: '', segs: segs };
    }
    var pairs = chatCatInfoPairs(d);
    segs.push(chatSegOutput(tool, '输出 · ' + pairs.length + ' 只猫', function (body, text, isErr) {
        chatSegKv(body, pairs);
    }, chatInfoPeek(pairs)));
    return { tag: '', tagCls: '', segs: segs };
}

// 全猫状态块 → 键值行（每猫一行：名(id)★ · 运行态:端口 · 相位/流式态 · 前文 · 活跃 · 轮次 · 消息 · 待处理 · Note）
function chatCatInfoPairs(d) {
    var pairs = [];
    var cats = d.cats;
    if (!cats || !cats.length) { return pairs; }
    for (var i = 0; i < cats.length; i = i + 1) {
        var c = cats[i];
        var name = chatField(c, 'name');
        var id = chatField(c, 'id');
        var head = name;
        if (id.length > 0 && id !== name) { head = name + '(' + id + ')'; }
        if (c.special === true) { head = head + '★'; }
        var parts = [];
        var port = chatFieldNum(c, 'port');
        if (c.running === true) {
            parts.push(port.length > 0 && port !== '0' ? '运行中 :' + port : '运行中');
        } else {
            parts.push('静默');
        }
        var phase = chatField(c, 'phase');
        var runState = chatField(c, 'runState');
        if (phase.length > 0) {
            // 相位（四相环）+ 流式态（七态）——空闲态不重复标注（idle 与 Idle 同义）
            parts.push(runState.length > 0 && runState !== 'idle' ? phase + '/' + runState : phase);
        }
        var ctx = chatFieldNum(c, 'context');
        var ctxCount = chatFieldNum(c, 'contextCount');
        if (ctx.length > 0) {
            parts.push('前文 ' + ctx + ' tokens' + (ctxCount.length > 0 ? ' / ' + ctxCount + ' 条' : ''));
        }
        var last = chatFieldNum(c, 'lastActiveAt');
        var ago = chatAgoText(last);
        if (ago.length > 0) { parts.push('活跃 ' + ago); }
        var round = chatFieldNum(c, 'round');
        if (round.length > 0) { parts.push('轮 ' + round); }
        var msgs = chatFieldNum(c, 'msgCount');
        if (msgs.length > 0) { parts.push('消息 ' + msgs); }
        var pending = chatFieldNum(c, 'pending');
        if (pending.length > 0 && pending !== '0') { parts.push('待处理 ' + pending); }
        if (c.noteActive === true) { parts.push('Note 激活'); }
        pairs.push({ k: head, v: parts.join(' · ') });
    }
    return pairs;
}

// 距今文本——Unix 毫秒 → 「N 秒/分钟/小时/天前」（无效值返回空串，该段不显示）
function chatAgoText(ms) {
    var t = Number(ms);
    if (!isFinite(t) || t <= 0) { return ''; }
    var diff = Date.now() - t;
    if (diff < 0) { diff = 0; }
    var sec = Math.floor(diff / 1000);
    if (sec < 60) { return sec + ' 秒前'; }
    var min = Math.floor(sec / 60);
    if (min < 60) { return min + ' 分钟前'; }
    var hour = Math.floor(min / 60);
    if (hour < 24) { return hour + ' 小时前'; }
    return Math.floor(hour / 24) + ' 天前';
}

// 分类块 → 键值行（固定顺序：猫 / 版本 / 当前时间 / LLM / 本地端点 / 可见根 / 前文 / 加载包 / QQBot）
// 缺类不显示该行（后端「无则省略键」约定——前端不补空行、不写 NaN）
function chatInfoPairs(d) {
    var pairs = [];
    var cat = chatField(d, 'cat');
    if (cat.length > 0) { pairs.push({ k: '猫', v: cat }); }
    var v = d.version;
    if (v && typeof v === 'object') {
        var ver = chatField(v, 'version');
        var build = chatField(v, 'build');
        pairs.push({ k: '版本', v: ver + (build.length > 0 ? ' · 编译 ' + build : '') });
    }
    var t = d.time;
    if (t && typeof t === 'object') {
        pairs.push({ k: '当前时间', v: chatField(t, 'now') });
    }
    var l = d.llm;
    if (l && typeof l === 'object') {
        var lp = [];
        var proto = chatField(l, 'protocol');
        var host = chatField(l, 'host');
        var model = chatField(l, 'model');
        var src = chatField(l, 'source');
        if (proto.length > 0) { lp.push(proto); }
        if (host.length > 0) { lp.push(host); }
        if (model.length > 0) { lp.push('model=' + model); }
        if (src.length > 0) { lp.push(src); }
        pairs.push({ k: 'LLM', v: lp.join(' · ') });
    }
    var ep = d.endpoint;
    if (ep && typeof ep === 'object') {
        var eps = [];
        var chat = chatField(ep, 'chat');
        var panel = chatField(ep, 'panel');
        if (chat.length > 0) { eps.push('对话 ' + chat); }
        if (panel.length > 0) { eps.push('管理面板 ' + panel); }
        pairs.push({ k: '本地端点', v: (eps.length > 0 ? eps.join(' · ') : '（未监听）') });
    }
    if (d.roots && d.roots.length > 0) {
        var roots = [];
        for (var i = 0; i < d.roots.length; i = i + 1) {
            var r = d.roots[i];
            var one = chatField(r, 'id') + '(' + (r.writable === true ? 'rw' : 'ro') + ')';
            var note = chatField(r, 'note');
            if (note.length > 0) { one = one + '[' + note + ']'; }
            roots.push(one);
        }
        pairs.push({ k: '可见根', v: roots.join(' · ') });
    }
    if (d.tokens && typeof d.tokens === 'object') {
        var ctx = chatFieldNum(d.tokens, 'context');
        if (ctx.length > 0) { pairs.push({ k: '前文', v: ctx + ' tokens' }); }
    }
    if (d.packs && d.packs.length > 0) {
        var packs = [];
        for (var k = 0; k < d.packs.length; k = k + 1) {
            var pk = chatField(d.packs[k], 'key');
            var desc = chatField(d.packs[k], 'desc');
            packs.push(desc.length > 0 ? pk + '(' + desc + ')' : pk);
        }
        pairs.push({ k: '加载包', v: packs.join(' · ') });
    }
    if (d.qqbot && typeof d.qqbot === 'object') {
        var usage = chatField(d.qqbot, 'usage');
        if (usage.length > 0) { pairs.push({ k: 'QQBot', v: usage }); }
    }
    return pairs;
}

// 取数值字段——非数值返回空串（键值行不写 NaN / 空值行）
function chatFieldNum(obj, key) {
    if (!obj || typeof obj[key] !== 'number') { return ''; }
    return String(obj[key]);
}

// 折叠摘要文本——键值行拼接（段折叠摘要在行数 >5 时展示「前 2 + … + 后 2」）
function chatInfoPeek(pairs) {
    var lines = [];
    for (var i = 0; i < pairs.length; i = i + 1) {
        lines.push(pairs[i].k + ': ' + pairs[i].v);
    }
    return lines.join('\n');
}

// 骨架实现表——骨架 id → 通用渲染
var CHAT_SKEL_RENDERERS = {
    'exec': chatSkelExec,
    'diagnostics': chatSkelDiagnostics,
    'listing': chatSkelListing,
    'matches': chatSkelMatches,
    'lines': chatSkelLines,
    'file': function (tool) { return chatSkelPlain(tool, true); },
    'json': chatSkelJson,
    'info': chatSkelInfo,
    'catinfo': chatSkelCatInfo,
    'text': function (tool) { return chatSkelPlain(tool, false); }
};

// 形态探测回落——完全未登记的工具：{ 可解析 → json 骨架；否则 text 骨架（禁止空白、禁止静默）
function chatSkelProbe(tool) {
    if (chatTryJson(tool.result)) { return chatSkelJson(tool); }
    return chatSkelPlain(tool, false);
}

// ═══════════════════════════════════════════
// 骨架图标——8 类语义大类各一个（折叠行图标按骨架取，不再逐工具自定）
// ═══════════════════════════════════════════

// 骨架图标表——与骨架清单一一对应（规格 §二）
var CHAT_SKEL_ICONS = {
    'exec': '💻',
    'diagnostics': '🩺',
    'listing': '📂',
    'matches': '🔍',
    'lines': '🔢',
    'file': '📖',
    'json': '🧩',
    'info': '🧭',
    'catinfo': '🐾',
    'text': '📝'
};

// 骨架中文名——兜底折叠行用（Z8：覆盖表未配 headline 的工具由骨架兜底接管）
var CHAT_SKEL_LABELS = {
    'exec': '命令执行',
    'diagnostics': '诊断',
    'listing': '列举',
    'matches': '检索',
    'lines': '按行读取',
    'file': '读取文件',
    'json': '结构查看',
    'info': '环境信息',
    'catinfo': '全猫状态',
    'text': '工具调用'
};

// 工具 → 骨架 id（未登记返回空串；探测骨架不参与图标——未知工具保持默认图标）
function chatToolSkeleton(name) {
    var skel = CHAT_TOOL_SKELETONS[name];
    return (typeof skel === 'string') ? skel : '';
}

// 工具图标——按骨架取；未登记骨架（含探测回落）返回空串，调用方回落默认图标
function chatToolIconBySkeleton(name) {
    var skel = chatToolSkeleton(name);
    if (skel.length === 0) { return ''; }
    var icon = CHAT_SKEL_ICONS[skel];
    return (typeof icon === 'string') ? icon : '';
}

// 工具专属图标表已并入覆盖表（CHAT_TOOL_OVERRIDES.icon）——此处不再单列表

// 工具图标解析——三级回落（规格 §二 图标口径）
// ① 工具专属（覆盖表 icon）→ ② 骨架图标（落位表 → CHAT_SKEL_ICONS）→ ③ ❓（未登记工具——本不该出现，显式暴露）
function chatToolIconOf(name) {
    var ov = chatToolOverride(name);
    if (ov && typeof ov.icon === 'string' && ov.icon.length > 0) { return ov.icon; }
    var icon = chatToolIconBySkeleton(name);
    if (icon.length > 0) { return icon; }
    return '❓';
}

// ═══════════════════════════════════════════
// 填充覆盖表——逐工具「肉」（全工具覆盖，按工具组推进）
// 定位：把工具参数与结果里的信息，按固定语句组合成自然语言（含折叠行 headline——Z8 起为唯一产出；宿主侧摘要已退役）
// 契约：只声明偏离骨架的字段——未声明项一律吃骨架默认（结构 / 折叠 / 三态永远归骨架）
//   icon        —— 折叠行专属图标（默认：骨架图标 → ❓）
//   tag/tagCls  —— 折叠行工具变体标签（如 PowerShell PS 5.1 / PS 7）
//   inputLines  —— 输入段行（自然语言意图；缺省 = 通用键值表）
//   outputLines —— 输出段行（缺省 = 骨架通用渲染）
//   badge       —— 输出段折叠头徽标后缀（缺省 = 骨架计数徽标）
// ═══════════════════════════════════════════

// 覆盖项取值——工具名 → 覆盖对象（无 / 非法 → null）
function chatToolOverride(name) {
    var ov = CHAT_TOOL_OVERRIDES[name];
    return (ov && typeof ov === 'object') ? ov : null;
}

// 缺值显式化——未传参不静默拼空串
function chatOvText(v) {
    if (v === undefined || v === null || v === '') { return '(未指定)'; }
    return String(v);
}

// 单行预览——多行内容进意图行（压平 + 60 字符截断）
function chatOvPeek(v) {
    if (typeof v !== 'string') { return chatOvText(v); }
    var t = v.replace(/\r/g, '').replace(/\n/g, ' ');
    return (t.length > 60) ? (t.substring(0, 60) + '…') : t;
}

// 多行文本字段值——取以 key 开头的行，返回其后值（去首尾空白；无匹配返回空串）
function chatOvLineField(r, key) {
    if (typeof r !== 'string' || r.length === 0) { return ''; }
    var lines = r.split('\n');
    for (var i = 0; i < lines.length; i = i + 1) {
        var t = lines[i];
        if (t.indexOf(key) === 0) {
            return t.substring(key.length).replace(/^\s+/, '').replace(/\s+$/, '');
        }
    }
    return '';
}

// 时长文本——{hours, minutes, seconds} → 「1 时 30 分」（全零 / 空对象 → 空串）
function chatOvDurText(o) {
    if (!o || typeof o !== 'object') { return ''; }
    var parts = [];
    var h = Number(o.hours) || 0;
    var mi = Number(o.minutes) || 0;
    var s = Number(o.seconds) || 0;
    if (h > 0) { parts.push(h + ' 时'); }
    if (mi > 0) { parts.push(mi + ' 分'); }
    if (s > 0) { parts.push(s + ' 秒'); }
    return parts.join(' ');
}

// 时刻文本——Unix 毫秒 → 本地「HH:mm:ss」（缺值 / 非法 → 空串）
function chatOvClock(ms) {
    if (typeof ms !== 'number' || ms <= 0) { return ''; }
    var d = new Date(ms);
    var hh = ('0' + d.getHours()).slice(-2);
    var mm = ('0' + d.getMinutes()).slice(-2);
    var ss = ('0' + d.getSeconds()).slice(-2);
    return hh + ':' + mm + ':' + ss;
}

// 规模——字符数
function chatOvSize(v) {
    return ((typeof v === 'string') ? v.length : 0) + ' 字符';
}

var CHAT_TOOL_OVERRIDES = {
    // ── TextCat（text-* 11 件）——折叠行自然语言 + 输入意图行 + 骨架输出 ──
    'text-read': {
        inputLines: function (a) { return ['读取 ' + chatOvText(a.path)]; },
        headline: function (a, r) { return '读取 ' + chatOvText(a.path) + chatOvStat(r); }
    },
    'text-read_between': {
        inputLines: function (a) {
            return ['区间读取 ' + chatOvText(a.path),
                '锚点 ' + chatOvText(a.str1 || '（文件头）') + ' ~ ' + chatOvText(a.str2 || '（文件尾）')];
        },
        headline: function (a, r) { return '区间读取 ' + chatOvText(a.path) + chatOvAnchor(a) + chatOvStat(r); }
    },
    'text-read_lines': {
        inputLines: function (a) {
            return ['按行读取 ' + chatOvText(a.path) + ' · ' + chatOvRange(a)];
        },
        headline: function (a, r) { return '按行读取 ' + chatOvText(a.path) + ' · ' + chatOvRange(a) + chatOvStat(r); }
    },
    'text-write': {
        inputLines: function (a) { return ['写入 ' + chatOvText(a.path) + ' · ' + chatOvSize(a.content)]; },
        headline: function (a, r) { return '写入 ' + chatOvText(a.path) + ' · ' + chatOvSize(a.content); }
    },
    'text-append': {
        inputLines: function (a) { return ['追加 ' + chatOvText(a.path) + ' · ' + chatOvSize(a.content)]; },
        headline: function (a, r) { return '追加 ' + chatOvText(a.path) + ' · +' + chatOvSize(a.content); }
    },
    'text-replace': {
        icon: '🔄',
        inputLines: function (a) {
            var head = '替换 ' + chatOvText(a.path) + (a.mode ? ' · 模式 ' + a.mode : '');
            return [head, '旧：' + chatOvPeek(a.old), '新：' + chatOvPeek(a.new)];
        },
        headline: function (a, r) { return '替换 ' + chatOvText(a.path) + chatOvMode(a) + chatOvReplaceTail(r); }
    },
    'text-grep': {
        inputLines: function (a) {
            var pat = a.pattern ? ' · 文件名 ' + a.pattern : '';
            return ['检索 ' + chatOvText(a.dir) + ' · 含 ' + chatOvText(a.keyword) + pat];
        },
        headline: function (a, r) {
            var pat = a.pattern ? ' · 文件名 ' + a.pattern : '';
            return '检索 ' + chatOvText(a.dir) + ' · 含 ' + chatOvText(a.keyword) + pat + ' · ' + chatOvItems(r) + ' 命中';
        }
    },
    // ── FileCat（A67 拆分——结构面独立组；2026-09-20）──
    'file-tree': {
        inputLines: function (a) {
            var extra = (a.limit === undefined) ? '' : ' · limit ' + a.limit;
            return ['展开 ' + chatOvText(a.path) + ' · depth ' + chatOvText(a.depth) + extra];
        },
        headline: function (a, r) {
            return '展开 ' + chatOvText(a.path) + ' · depth ' + chatOvText(a.depth) + ' · ' + chatOvItems(r) + ' 条目' + chatOvTotalTail(r);
        }
    },
    'file-find': {
        icon: '🔍',
        inputLines: function (a) { return ['搜索 ' + chatOvText(a.dir) + ' · glob ' + chatOvText(a.pattern)]; },
        headline: function (a, r) {
            return '搜索 ' + chatOvText(a.dir) + ' · glob ' + chatOvText(a.pattern) + ' · ' + chatOvItems(r) + ' 条目' + chatOvTotalTail(r);
        }
    },
    'file-move': {
        icon: '📦',
        inputLines: function (a) { return ['移动 ' + chatOvText(a.src), '→ ' + chatOvText(a.dest)]; },
        headline: function (a, r) { return '移动 ' + chatOvText(a.src) + ' → ' + chatOvText(a.dest); }
    },
    'file-delete': {
        icon: '🗑️',
        inputLines: function (a) { return ['删除 ' + chatOvText(a.path) + ' → 回收站']; },
        headline: function (a, r) { return '删除 ' + chatOvText(a.path) + ' → 回收站'; }
    },
    'file-copy': {
        icon: '📋',
        inputLines: function (a) { return ['复制 ' + chatOvText(a.src), '→ ' + chatOvText(a.dest)]; },
        headline: function (a, r) { return '复制 ' + chatOvText(a.src) + ' → ' + chatOvText(a.dest); }
    },
    'file-version': {
        inputLines: function (a) { return ['读取版本信息 ' + chatOvText(a.path)]; },
        headline: function (a, r) {
            var name = chatOvShort(a.path);
            var v = chatOvLineField(r, '版本:');
            var plus = v.indexOf('+');
            if (plus > 0) { v = v.substring(0, plus); }
            if (v.length > 0) { return '版本信息 ' + name + ' · ' + v; }
            return '版本信息 ' + name + chatOvStat(r);
        }
    },
    // ── CsCat（样板三件——结构化返回头驱动；2026-09-18）──
    'cs-check': {
        inputLines: function (a) {
            return ['语法检查 ' + chatOvText(a.path) + (a.full === true ? ' · 含警告' : '')];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '语法检查 ' + chatOvShort(a.path) + chatOvStat(r); }
            return '语法检查 ' + chatOvShort(a.path) + ' · ' + (h.meta.errors || 0) + ' 错 ' + (h.meta.warnings || 0) + ' 警';
        }
    },
    'cs-build': {
        inputLines: function (a) { return ['编译 ' + chatOvText(a.path)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '编译 ' + chatOvShort(a.path) + chatOvStat(r); }
            var ms = (typeof h.meta.ms === 'number') ? (' · ' + (h.meta.ms / 1000).toFixed(1) + 's') : '';
            return '编译 ' + chatOvShort(a.path) + ' · ' + (h.meta.ok ? '成功' : '失败') + ' ' + (h.meta.errors || 0) + ' 错 ' + (h.meta.warnings || 0) + ' 警' + ms;
        }
    },
    'cs-read': {
        inputLines: function (a) {
            var target = chatOvText(a.class) + (a.member ? '.' + a.member : '');
            return ['读取 ' + target, '项目 ' + chatOvText(a.path)];
        },
        headline: function (a, r) {
            var target = chatOvText(a.class) + (a.member ? '.' + a.member : '');
            var h = chatMetaHead(r);
            if (!h) { return '读取 ' + target + chatOvStat(r); }
            var range = (typeof h.meta.start === 'number') ? (' · L' + h.meta.start + '-' + h.meta.end) : '';
            return '读取 ' + target + range;
        }
    },
    'cs-list': {
        inputLines: function (a) {
            return [a.class ? ('列出 ' + chatOvText(a.path) + ' · 类 ' + a.class) : ('列出 ' + chatOvText(a.path) + ' 的类')];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '列出 ' + chatOvShort(a.path) + chatOvStat(r); }
            return '列出 ' + (h.meta.project || chatOvShort(a.path)) + ' · ' + (h.meta.classes || 0) + ' 类';
        }
    },
    'cs-find_ref': {
        inputLines: function (a) { return ['查找引用 ' + chatOvText(a.class) + '.' + chatOvText(a.member)]; },
        headline: function (a, r) {
            var target = chatOvText(a.class) + '.' + chatOvText(a.member);
            var h = chatMetaHead(r);
            if (!h) { return '引用 ' + target + chatOvStat(r); }
            return '引用 ' + target + ' · ' + (h.meta.hits || 0) + ' 处 · ' + (h.meta.projects || 0) + ' 项目';
        }
    },
    'cs-dead': {
        inputLines: function (a) { return ['扫描零引用 ' + chatOvText(a.path)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '零引用扫描 ' + chatOvShort(a.path) + chatOvStat(r); }
            var dead = h.meta.dead || 0;
            return '零引用扫描 ' + chatOvShort(a.path) + ' · ' + (dead > 0 ? (dead + ' 处待清') : '干净');
        }
    },
    'cs-comment_check': {
        inputLines: function (a) { return ['检查注释 ' + chatOvText(a.path)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '注释检查 ' + chatOvShort(a.path) + chatOvStat(r); }
            var miss = h.meta.missing || 0;
            return '注释检查 ' + chatOvShort(a.path) + ' · ' + (miss > 0 ? (miss + ' 处缺 summary') : ('齐全 ' + (h.meta.checked || 0) + ' 项'));
        }
    },
    'cs-format': {
        inputLines: function (a) {
            var mode = (a.mode === 'apply') ? 'apply' : 'check';
            return ['格式' + (mode === 'apply' ? '规整' : '检查') + ' ' + chatOvText(a.path) + ' · mode=' + mode];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '格式哨兵 ' + chatOvShort(a.path) + chatOvStat(r); }
            var m = h.meta;
            var act = (m.mode === 'apply') ? '规整' : '需规整';
            var tail = (m.failedFiles > 0) ? (' · ' + m.failedFiles + ' 文件校验未过') : '';
            return '格式哨兵 ' + chatOvShort(a.path) + ' · ' + act + ' ' + (m.changedFiles || 0) + ' 文件 / ' + (m.changedLines || 0) + ' 行' + tail;
        }
    },
    'cs-patch': {
        inputLines: function (a) {
            return ['改写 ' + chatOvText(a.class) + '.' + chatOvText(a.method) + ' 方法体', '项目 ' + chatOvText(a.path)];
        },
        headline: function (a, r) {
            var target = chatOvText(a.class) + '.' + chatOvText(a.method);
            var h = chatMetaHead(r);
            if (!h) { return '改写 ' + target + chatOvStat(r); }
            var m = h.meta;
            var range = (typeof m.start === 'number') ? (' · L' + m.start + '-' + m.end) : '';
            return '改写 ' + target + ' · ' + (m.state || 'OK') + range;
        },
        outputLines: function (r) {
            var h = chatMetaHead(r);
            if (!h) { return null; }
            var body = h.body;
            if (typeof body !== 'string' || body.length === 0) { return ['已落盘 ' + (h.meta.file || '')]; }
            var lines = chatLines(body);
            var out = [];
            for (var i = 0; i < lines.length; i++) { out.push(chatStripLineMark(lines[i])); }
            return out;
        }
    },
    'cs-member': {
        inputLines: function (a) {
            var op = a.op || '';
            if (op === 'insert') {
                if (a.codes && a.codes.length) { return ['批量插入 ' + a.codes.length + ' 个成员到 ' + chatOvText(a.class) + ' · 位置 ' + chatOvText(a.position)]; }
                return ['插入成员到 ' + chatOvText(a.class) + ' · 位置 ' + chatOvText(a.position)];
            }
            if (op === 'delete') { return ['删除成员 ' + chatOvText(a.class) + '.' + chatOvText(a.member)]; }
            if (op === 'rename') { return ['重命名 ' + chatOvText(a.class) + '.' + chatOvText(a.oldName) + ' → ' + chatOvText(a.newName)]; }
            return ['成员操作 ' + chatOvText(a.class) + ' · op=' + chatOvText(a.op)];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '成员操作 ' + chatOvText(a.class) + chatOvStat(r); }
            var m = h.meta;
            if (m.op === 'insert') {
                if (m.items && m.items.length) {
                    return '批量插入 ' + m.count + ' 个成员 · 落盘 L' + m.items[0].start + '-' + m.items[m.items.length - 1].end;
                }
                return '插入 ' + m.class + ' · 落盘 L' + m.start + '-' + m.end;
            }
            if (m.op === 'delete') { return '删除 ' + m.class + '.' + m.member; }
            if (m.op === 'rename') { return '重命名 ' + m.oldName + ' → ' + m.newName + ' · ' + m.files + ' 文件'; }
            return '成员操作 ' + chatOvText(a.class);
        },
        outputLines: function (r) {
            var h = chatMetaHead(r);
            if (!h) { return null; }
            var m = h.meta;
            if (m.op === 'insert') {
                if (m.items && m.items.length) {
                    var bulk = [];
                    for (var bi = 0; bi < m.items.length; bi++) {
                        bulk.push('#' + (bi + 1) + ' L' + m.items[bi].start + '-' + m.items[bi].end + ' · ' + (m.items[bi].kind || ''));
                    }
                    return bulk;
                }
                return ['已落盘 ' + (m.file || '') + ' · L' + m.start + '-' + m.end + ' · ' + (m.kind || '')];
            }
            if (m.op === 'delete') { return ['已删除 ' + m.class + '.' + m.member + '（' + (m.file || '') + '）']; }
            if (m.op === 'rename') { return ['已重命名 ' + m.oldName + ' → ' + m.newName + ' · ' + (m.files || 0) + ' 文件']; }
            return ['（已应用）'];
        }
    },
    'cs-comment': {
        inputLines: function (a) {
            var target = chatOvText(a.class) + (a.member ? '.' + a.member : '');
            return ['写 ' + chatOvText(a.type) + ' 注释 → ' + target];
        },
        headline: function (a, r) {
            var target = chatOvText(a.class) + (a.member ? '.' + a.member : '');
            var h = chatMetaHead(r);
            if (!h) { return '注释 ' + target + chatOvStat(r); }
            return '注释 ' + target + ' · ' + (h.meta.type || '');
        },
        outputLines: function (r) {
            var h = chatMetaHead(r);
            if (!h) { return null; }
            var m = h.meta;
            return ['已写入 ' + (m.class || '') + (m.member ? '.' + m.member : '') + ' 的 ' + (m.type || '') + ' 注释'];
        }
    },
    // ── PsCat（powershell 双线——exec 骨架 + 版本标签）──
    'powershell': { tag: 'PS 5.1', tagCls: 'ps5' },
    'powershell7': { tag: 'PS 7', tagCls: 'ps7' },
    // ── SearchCat / VisionCat / TempToolCat / Majordomo（A64 批 1——结构化头驱动；2026-09-18）──
    'web-search': {
        icon: '🌐',
        inputLines: function (a) { return ['联网搜索 ' + chatOvText(a.query)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '联网搜索 ' + chatOvText(a.query) + chatOvStat(r); }
            var cite = (typeof h.meta.citations === 'number' && h.meta.citations > 0) ? (' · ' + h.meta.citations + ' 条引用') : '';
            var proto = h.meta.protocol ? (' · ' + h.meta.protocol) : '';
            return '联网搜索 ' + chatOvText(a.query) + cite + proto;
        }
    },
    'image-analyze': {
        // A65——输入图片预览（被识别的图直接看得见；路径形态交 chatImgUrl 单一出口）
        inputImages: function (a) {
            if (!a || !a.path) { return []; }
            return [a.path];
        },
        inputLines: function (a) {
            return ['识别图片 ' + chatOvText(a.path),
                (a.question ? ('提示词 ' + chatOvPeek(a.question)) : '（默认描述）')];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            var name = chatOvShort(a.path);
            if (!h) { return '识别图片 ' + name + chatOvStat(r); }
            return '识别图片 ' + name + ' · ' + (h.meta.chars || 0) + ' 字';
        }
    },
    'temp-info': {
        inputLines: function () { return ['列出临时工具 Key（TempRegistry）']; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '临时工具 Key ' + chatOvStat(r); }
            var n = h.meta.count || 0;
            return '临时工具 · ' + (n > 0 ? (n + ' 个可用 Key') : '暂无注册');
        }
    },
    'temp-exec': {
        inputLines: function (a) { return ['临时执行 ' + chatOvText(a.key) + ' · 入参 ' + chatOvSize(a.content)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '临时执行 ' + chatOvText(a.key) + chatOvStat(r); }
            return '临时执行 ' + h.meta.key + ' · ' + (h.meta.chars || 0) + ' 字';
        }
    },
    'majordomo-restart': {
        inputLines: function () { return ['请求宿主自更新（部署 + 重启）']; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '宿主重启 ' + chatOvStat(r); }
            return '宿主重启 · 目标 ' + (h.meta.target || '默认') + (h.meta.push ? ' · 带回执' : '');
        }
    },
    'majordomo-cmd': {
        inputLines: function (a) { return ['宿主指令 ' + chatOvText(a.cmd)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '宿主指令 ' + chatOvStat(r); }
            return '宿主指令 ' + chatOvShort(a.cmd);
        }
    },
    // ── ConfigCat（A64 批 2——结构化头驱动；2026-09-18）──
    'config-list': {
        inputLines: function () { return ['列出全部配置项（schema 声明 + 落盘未声明）']; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '配置列表 ' + chatOvStat(r); }
            var m = h.meta;
            return '配置列表 · ' + (m.count || 0) + ' 项 · ' + (m.writable || 0) + ' 可写';
        }
    },
    'config-get': {
        inputLines: function (a) { return ['读取配置 ' + chatOvText(a.key)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '读取配置 ' + chatOvText(a.key) + chatOvStat(r); }
            var m = h.meta;
            var src = m.source ? (' · 来源 ' + m.source) : '';
            var ro = (m.declared === true && m.writable === false) ? ' · 只读' : '';
            return '读取配置 ' + m.key + src + ro;
        }
    },
    'config-set': {
        inputLines: function (a) { return ['设置配置 ' + chatOvText(a.key) + ' = ' + chatOvPeek(a.value)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '设置配置 ' + chatOvText(a.key) + chatOvStat(r); }
            return '设置配置 ' + h.meta.key + ' · 已更新';
        }
    },
    'config-reset': {
        inputLines: function (a) {
            return [a.key ? ('还原配置 ' + a.key + ' 为默认') : '还原全部可写配置为默认'];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '还原配置 ' + chatOvStat(r); }
            var m = h.meta;
            if (m.scope === 'all') { return '还原全部可写配置 · ' + (m.count || 0) + ' 项'; }
            return '还原配置 ' + (m.key || '') + ' · 默认值';
        }
    },
    'config-cat-get': {
        inputLines: function (a) { return ['读取每猫配置 ' + chatOvText(a.cat)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '每猫配置 ' + chatOvText(a.cat) + chatOvStat(r); }
            return '每猫配置 ' + h.meta.cat + ' · 全量字段';
        }
    },
    'config-cat-set': {
        inputLines: function (a) { return ['设置每猫配置 ' + chatOvText(a.cat) + ' · ' + chatOvText(a.field)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '每猫配置 ' + chatOvText(a.cat) + chatOvStat(r); }
            return '每猫配置 ' + h.meta.cat + ' · ' + chatOvText(a.field) + ' 已更新';
        }
    },
    // ── MauCat（A64 批 3——结构化头驱动；2026-09-18）──
    'mau-verify': {
        inputLines: function (a) { return ['全链检查 ' + chatOvText(a.file)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return 'Mau 验证 ' + chatOvShort(a.file) + chatOvStat(r); }
            var m = h.meta;
            if (m.ok === false) { return 'Mau 验证 ' + chatOvShort(a.file) + ' · ' + (m.errors || 0) + ' 个错误'; }
            return 'Mau 验证 ' + chatOvShort(a.file) + ' · 通过（' + (m.reports || 0) + ' 报告）';
        }
    },
    'mau-gen': {
        inputLines: function (a) { return ['组翻译（不编译）' + chatOvText(a.proj)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return 'Mau 生成 ' + chatOvShort(a.proj) + chatOvStat(r); }
            var m = h.meta;
            if (m.ok === false) { return 'Mau 生成 ' + m.proj + ' · ' + (m.errors || 0) + ' 个错误'; }
            return 'Mau 生成 ' + m.proj + ' · ' + (m.steps || 0) + ' 步';
        }
    },
    'mau-proj': {
        inputLines: function (a) {
            return ['组翻译 + 编译 ' + chatOvText(a.proj) + (a.build === true ? ' · build' : '')];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '组翻译 ' + chatOvShort(a.proj) + chatOvStat(r); }
            var m = h.meta;
            if (m.ok === false) { return '组翻译 ' + m.proj + ' · ' + (m.errors || 0) + ' 个错误'; }
            return '组翻译 ' + m.proj + ' · ' + (m.steps || 0) + ' 步' + (m.build ? ' · 已编译' : '');
        }
    },
    'mau-setup': {
        inputLines: function (a) {
            var mode = a.mode || 'prepare';
            var extra = a.target ? (' · 目标 ' + a.target) : '';
            return ['一键部署 ' + mode + extra];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '一键部署 ' + chatOvStat(r); }
            var m = h.meta;
            var base = '一键部署 ' + (m.mode || '') + ' · ' + (m.stepsOk || 0) + '/' + (m.steps || 0) + ' 步 · ' + (m.artifacts || 0) + ' 产物';
            if (m.ok === false) { return base + ' · 失败'; }
            return base;
        }
    },
    // ── 内置 7 件（A64 批 4——结构化头驱动；2026-09-18）──
    'Note': {
        inputLines: function (a) {
            if (a.action === 'set') { return ['写入计划 · ' + chatOvSize(a.content) + (a.force === true ? ' · 强制覆盖' : '')]; }
            return ['推进到下一条任务'];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '任务追踪 ' + chatOvStat(r); }
            var m = h.meta;
            if (m.state === 'done') { return '任务追踪 · 全部完成（' + (m.total || 0) + ' 条）'; }
            if (m.state === 'empty') { return '任务追踪 · 暂无计划'; }
            return '任务追踪 · 第' + m.index + '/' + m.total + '条 · 已完成' + m.done + ' 待完成' + m.remain;
        }
    },
    'time': {
        inputLines: function () { return ['获取当前时间']; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '时间 ' + chatOvStat(r); }
            return '时间 · ' + (h.meta.ts || '');
        }
    },
    'random': {
        inputLines: function (a) { return ['随机数 ' + chatOvText(a.min) + ' ~ ' + chatOvText(a.max)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '随机数 ' + chatOvStat(r); }
            var m = h.meta;
            return '随机数 [' + chatOvText(m.min) + ',' + chatOvText(m.max) + ') → ' + chatOvText(m.value);
        }
    },
    'info': {
        inputLines: function () { return ['查看本会话运行环境']; },
        headline: function (a, r) {
            // 返回体 = 分类 JSON 块（无「头 + 正文」两段）——整块解析，不用 chatMetaHead
            var d = chatTryJson(r);
            if (!d) { return '环境信息 ' + chatOvStat(r); }
            var ver = '';
            if (d.version && typeof d.version === 'object') { ver = chatField(d.version, 'version'); }
            var cat = chatField(d, 'cat');
            var t = '环境信息 · v' + ver;
            if (cat.length > 0) { t = t + ' · 猫 ' + cat; }
            return t;
        }
    },
    'majordomo-catinfo': {
        inputLines: function () { return ['查看全猫状态统计']; },
        headline: function (a, r) {
            // 返回体 = 分类 JSON 块（无「头 + 正文」两段）——整块解析，不用 chatMetaHead
            var d = chatTryJson(r);
            if (!d) { return '全猫状态 ' + chatOvStat(r); }
            var n = chatFieldNum(d, 'count');
            if (n.length === 0) { return '全猫状态'; }
            return '全猫状态 · ' + n + ' 只猫';
        }
    },
    'host-reload': {
        inputLines: function (a) { return ['热重载组 ' + chatOvText(a.cat)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '热重载 ' + chatOvText(a.cat) + chatOvStat(r); }
            var m = h.meta;
            return '热重载 ' + m.cat + ' · #' + m.oldId + ' → #' + m.newId;
        }
    },
    'host-flows': {
        inputLines: function () { return ['查看当前运行 Flow 清单']; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return 'Flow 现状 ' + chatOvStat(r); }
            return 'Flow 现状 · ' + (h.meta.count || 0) + ' 个';
        }
    },
    // ── 延迟指令（A71/A72——sleep 等待 / timer 排程；结构化头驱动；2026-09-28）──
    'sleep': {
        inputLines: function (a) {
            var d = chatOvDurText(a);
            return ['登记定时唤醒' + ((d.length > 0) ? (' · ' + d) : '')];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '定时唤醒 ' + chatOvStat(r); }
            var d = chatOvDurText(h.meta);
            var clock = chatOvClock(h.meta.dueAt);
            return '定时唤醒 · ' + ((d.length > 0) ? d : '待唤醒') + ((clock.length > 0) ? (' · 到点 ' + clock) : '');
        }
    },
    'timer': {
        inputLines: function (a) {
            var d = chatOvDurText(a);
            var loop = (a.loop === true) ? ' · 循环' : '';
            return ['登记定时注入' + ((d.length > 0) ? (' · ' + d) : '') + loop, '内容 ' + chatOvPeek(a.content)];
        },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '定时注入 ' + chatOvStat(r); }
            var d = chatOvDurText(h.meta);
            var loop = (h.meta.loop === true) ? ' · 循环' : '';
            var clock = chatOvClock(h.meta.dueAt);
            return '定时注入 · ' + ((d.length > 0) ? d : '待注入') + loop + ((clock.length > 0) ? (' · 到点 ' + clock) : '');
        }
    },
    'pack': {
        inputLines: function (a) { return ['加载包 ' + chatOvText(a.key)]; },
        headline: function (a, r) {
            var h = chatMetaHead(r);
            if (!h) { return '加载包 ' + chatOvText(a.key) + chatOvStat(r); }
            var m = h.meta;
            return '加载包 ' + m.key + ' · ' + (m.files || 0) + ' 件';
        }
    }
};

// ═══════════════════════════════════════════
// 折叠行文案（headline）——前端按固定语句组合的自然语言摘要
// 口径（2026-09-18 莎定；2026-09-28 Z8 修订）：覆盖表 headline 为折叠行唯一产出；
// 未配 headline 的工具由骨架兜底（chatToolFallbackHeadline）接管；有 headline 时不追加结果规模后缀（同一语义不两处实现）
// ═══════════════════════════════════════════

// 折叠行文案取值——覆盖表 headline 优先；无则返回空串（调用方回落骨架兜底 chatToolFallbackHeadline）
function chatToolHeadline(tool) {
    var ov = chatToolOverride(tool.name);
    if (ov && typeof ov.headline === 'function') {
        var t = ov.headline(chatArgObj(tool), tool.result);
        if (typeof t === 'string' && t.length > 0) { return t; }
    }
    return '';
}

// 骨架兜底折叠行——覆盖表未配 headline 时接管（Z8：宿主 summary 退役后的唯一兜底）
// 口径：骨架中文名 + 工具名——骨架只决定壳、不猜工具语义；未登记骨架的工具回落工具名本身
function chatToolFallbackHeadline(tool) {
    if (!tool || typeof tool.name !== 'string' || tool.name.length === 0) { return '?'; }
    var skel = chatToolSkeleton(tool.name);
    var label = CHAT_SKEL_LABELS[skel];
    if (typeof label !== 'string') { return tool.name; }
    return label + ' · ' + tool.name;
}

// 结果行数——空结果 0
function chatOvLines(r) {
    if (typeof r !== 'string' || r.length === 0) { return 0; }
    return r.split('\n').length;
}

// 结果规模后缀——「 · N 行 M 字符」（未完成不标；空结果显式「 · 无输出」）
function chatOvStat(r) {
    if (r === undefined) { return ''; }
    if (typeof r !== 'string' || r.length === 0) { return ' · 无输出'; }
    return ' · ' + chatOvLines(r) + ' 行 ' + chatFmtCount(r.length) + ' 字符';
}

// 结果条目数——空行与提示行（[git] / [skip] / [截断]）不计
function chatOvItems(r) {
    if (typeof r !== 'string' || r.length === 0) { return 0; }
    var lines = r.split('\n');
    var n = 0;
    for (var i = 0; i < lines.length; i++) {
        var t = lines[i];
        if (t.replace(/\s/g, '').length === 0) { continue; }
        if (t.indexOf('[git]') === 0 || t.indexOf('[skip]') === 0 || t.indexOf('[截断]') === 0) { continue; }
        n = n + 1;
    }
    return n;
}

// 截断总量——结果含「[截断] 共 N 条」时返回 N，否则 0
function chatOvTotal(r) {
    if (typeof r !== 'string' || r.length === 0) { return 0; }
    var m = /\[截断\] 共 (\d+) 条/.exec(r);
    return m ? parseInt(m[1], 10) : 0;
}

// 截断总量后缀——「（共 N 条）」；无截断返回空串
function chatOvTotalTail(r) {
    var t = chatOvTotal(r);
    return t > 0 ? '（共 ' + t + ' 条）' : '';
}

// 结果中提取首个「N <单位>」计数——替换处数等固定语句用（提不到返回空串）
function chatOvCount(r, unit) {
    if (typeof r !== 'string' || r.length === 0) { return ''; }
    var m = new RegExp('(\\d+)\\s*' + unit).exec(r);
    return m ? m[1] : '';
}

// 区间锚点描述——「 · 锚点 A ~ B」（两端皆缺则省略）
function chatOvAnchor(a) {
    if (!a.str1 && !a.str2) { return ''; }
    return ' · 锚点 ' + (a.str1 || '（文件头）') + ' ~ ' + (a.str2 || '（文件尾）');
}

// 行区间描述——「L1~20」/「L5 起」
function chatOvRange(a) {
    return 'L' + chatOvText(a.start) + (a.end === undefined ? ' 起' : '~' + a.end);
}

// 替换模式描述——「 · 模式 exact」（未传省略）
function chatOvMode(a) {
    return a.mode ? ' · 模式 ' + a.mode : '';
}

// 替换结果尾巴——「 · N 处」/「 · 失败」（无可标则不标）
function chatOvReplaceTail(r) {
    var cnt = chatOvCount(r, '处');
    if (cnt.length > 0) { return ' · ' + cnt + ' 处'; }
    if (typeof r === 'string' && r.indexOf('ERR') === 0) { return ' · 失败'; }
    return '';
}

// ═══════════════════════════════════════════
// 结构化返回头——工具返回体「首行 JSON 元数据 + 正文定界行」的拆分（design-ch4-tools 附录）
// 后端约定：cs-* 返回首行 JSON（含 tool 字段）+ 其后正文；未结构化结果返回 null（走旧路径）
// ═══════════════════════════════════════════

// 拆分——首行 JSON 含 tool 字段 → {meta, body}；否则 null（旧格式 / 非结构化结果，调用方按原文处理）
function chatMetaHead(text) {
    if (typeof text !== 'string' || text.charAt(0) !== '{') { return null; }
    var nl = text.indexOf('\n');
    var first = (nl < 0) ? text : text.substring(0, nl);
    var obj = chatTryJson(first);
    if (!obj || typeof obj.tool !== 'string') { return null; }
    return { meta: obj, body: (nl < 0) ? '' : text.substring(nl + 1) };
}

// 路径短名——取末段（折叠行可读性；全路径仍在输入段）
function chatOvShort(p) {
    var t = chatOvText(p);
    var i = Math.max(t.lastIndexOf('/'), t.lastIndexOf('\\'));
    return (i >= 0 && i + 1 < t.length) ? t.substring(i + 1) : t;
}

// 行尾行号标注剥离——cs-read 正文行尾 `// L{行号}`（LLM 定位用）；前端已有行号列，显示时剥离
function chatStripLineMark(line) {
    if (typeof line !== 'string') { return ''; }
    return line.replace(/\s*\/\/ L\d+\s*$/, '');
}

// 结构化头计数——diagnostics 骨架用（errors / warnings 缺失 → null）
function chatDiagCountsFromMeta(meta) {
    if (!meta) { return null; }
    var e = (typeof meta.errors === 'number') ? meta.errors : -1;
    var w = (typeof meta.warnings === 'number') ? meta.warnings : -1;
    if (e < 0 && w < 0) { return null; }
    return { errors: (e < 0 ? 0 : e), warnings: (w < 0 ? 0 : w) };
}
