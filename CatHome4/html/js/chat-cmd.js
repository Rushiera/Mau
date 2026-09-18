// CH4 外观层——chat-cmd.js：PowerShell 命令解读器（纯函数，无依赖）
// 定位：powershell 工具 command 原文硬解码为自然语言意图——工具卡折叠行 + 展开区（可见性适配）
// 消费：chat-view.js chatToolCard（工具名 = powershell 时覆盖宿主 summary）
// 原则：表驱动确定性解析——零 LLM / 零网络 / 零状态；未识别段原样标注（不编造、不静默）
// 加载顺序：chat-cmd.js → chat-view.js → chat-core.js（chat.html 引导层引用）

// ═══════════════════════════════════════════
// 入口——工具卡消费面
// ═══════════════════════════════════════════

/**
 * 工具参数原文 → 命令解读。
 * @param {string} argsText 工具 arguments（JSON 字符串；实时 payload 可能被宿主截断）
 * @returns {object|null} {brief, detail, truncated, unknown}；无法解析返回 null（调用方回退宿主 summary）
 */
function cmdDecodeTool(argsText) {
    var got = cmdExtractCommand(argsText);
    if (got === null) {
        return null;
    }
    if (got.command.replace(/\s/g, '').length === 0) {
        return null;
    }
    var segs = cmdSplitSegments(got.command);
    if (segs.length === 0) {
        return null;
    }
    var intents = [];
    for (var i = 0; i < segs.length; i = i + 1) {
        intents.push(cmdSegmentIntent(segs[i].text, segs[i].pipe));
    }
    return {
        brief: cmdBrief(intents, got.truncated),
        detail: cmdDetail(intents, got.truncated),
        truncated: got.truncated,
        unknown: cmdUnknownItems(intents)
    };
}

// ═══════════════════════════════════════════
// 参数面——argsJSON → command 原文
// ═══════════════════════════════════════════

/**
 * 提取 command 字段——完整 JSON 优先，失败回退正则提取（宿主截断 200 字时 JSON 未闭合）。
 * @param {string} argsText 工具参数原文
 * @returns {object|null} {command, truncated}
 */
function cmdExtractCommand(argsText) {
    if (!argsText || argsText.length === 0) {
        return null;
    }
    if (argsText.charAt(0) === '{') {
        try {
            var obj = JSON.parse(argsText);
            if (obj && typeof obj.command === 'string') {
                return { command: obj.command, truncated: false };
            }
            return null;
        } catch (e) {
            // 截断/损坏——走正则兜底
        }
    }
    var m = /"command"\s*:\s*"((?:[^"\\]|\\.)*)/.exec(argsText);
    if (!m) {
        return null;
    }
    return { command: cmdUnescapeJson(m[1]), truncated: true };
}

/**
 * JSON 字符串转义还原（正则兜底路径用）。
 * @param {string} s 原始转义片段
 * @returns {string} 还原文本
 */
function cmdUnescapeJson(s) {
    var out = '';
    var i = 0;
    while (i < s.length) {
        var c = s.charAt(i);
        if (c !== '\\' || i + 1 >= s.length) {
            out = out + c;
            i = i + 1;
            continue;
        }
        var n = s.charAt(i + 1);
        if (n === 'n') { out = out + '\n'; i = i + 2; }
        else if (n === 'r') { out = out + '\r'; i = i + 2; }
        else if (n === 't') { out = out + '\t'; i = i + 2; }
        else if (n === '"') { out = out + '"'; i = i + 2; }
        else if (n === '\\') { out = out + '\\'; i = i + 2; }
        else if (n === '/') { out = out + '/'; i = i + 2; }
        else if (n === 'u' && i + 5 < s.length) {
            out = out + String.fromCharCode(parseInt(s.substring(i + 2, i + 6), 16));
            i = i + 6;
        } else {
            out = out + n;
            i = i + 2;
        }
    }
    return out;
}

// ═══════════════════════════════════════════
// 切分——引号感知断句
// ═══════════════════════════════════════════

/**
 * 命令切分——分隔符 ; && || | 换行（引号内不切）。
 * @param {string} cmd 命令全文
 * @returns {array} [{text, pipe}]——pipe=本段由 | 接续而来
 */
function cmdSplitSegments(cmd) {
    var segs = [];
    var buf = '';
    var inSingle = false;
    var inDouble = false;
    var curPipe = false;
    var i = 0;
    while (i < cmd.length) {
        var c = cmd.charAt(i);
        if (inSingle) {
            buf = buf + c;
            if (c === '\'') {
                if (i + 1 < cmd.length && cmd.charAt(i + 1) === '\'') {
                    buf = buf + '\'';
                    i = i + 2;
                    continue;
                }
                inSingle = false;
            }
            i = i + 1;
            continue;
        }
        if (inDouble) {
            buf = buf + c;
            if (c === '`' && i + 1 < cmd.length) {
                buf = buf + cmd.charAt(i + 1);
                i = i + 2;
                continue;
            }
            if (c === '"') {
                inDouble = false;
            }
            i = i + 1;
            continue;
        }
        if (c === '\'') { inSingle = true; buf = buf + c; i = i + 1; continue; }
        if (c === '"') { inDouble = true; buf = buf + c; i = i + 1; continue; }
        var sep = null;
        var step = 1;
        if (c === ';') { sep = ';'; }
        else if (c === '\n' || c === '\r') { sep = 'nl'; }
        else if (c === '&' && i + 1 < cmd.length && cmd.charAt(i + 1) === '&') { sep = 'and'; step = 2; }
        else if (c === '|') {
            if (i + 1 < cmd.length && cmd.charAt(i + 1) === '|') { sep = 'or'; step = 2; }
            else { sep = 'pipe'; }
        }
        if (sep !== null) {
            var t = cmdTrim(buf);
            if (t.length > 0) {
                segs.push({ text: t, pipe: curPipe });
            }
            curPipe = (sep === 'pipe');
            buf = '';
            i = i + step;
            continue;
        }
        buf = buf + c;
        i = i + 1;
    }
    var last = cmdTrim(buf);
    if (last.length > 0) {
        segs.push({ text: last, pipe: curPipe });
    }
    return segs;
}

// ═══════════════════════════════════════════
// 单段解读
// ═══════════════════════════════════════════

/**
 * 单段命令 → 意图。
 * @param {string} segText 段原文
 * @param {boolean} piped 是否由管道接续
 * @returns {object} {known, text, piped}
 */
function cmdSegmentIntent(segText, piped) {
    var suffix = '';
    var body = segText;
    var redir = /\s(>>?)\s*([^>|]*)$/.exec(body);
    if (redir) {
        body = cmdTrim(body.substring(0, redir.index));
        suffix = '（结果写入文件 ' + cmdQuote(cmdTrim(redir[2])) + '）';
    }
    var clean = cmdStripCall(body);
    for (var i = 0; i < CMD_RULES.length; i = i + 1) {
        var m = CMD_RULES[i].re.exec(clean);
        if (m) {
            return { known: true, text: CMD_RULES[i].fmt(clean, m) + suffix, piped: piped === true };
        }
    }
    return { known: false, text: clean + suffix, piped: piped === true };
}

/** 去前导调用运算符 & 与空白 */
function cmdStripCall(text) {
    var t = cmdTrim(text);
    while (t.length > 0 && t.charAt(0) === '&' && t.charAt(1) !== '&') {
        t = cmdTrim(t.substring(1));
    }
    return t;
}

// ═══════════════════════════════════════════
// 折叠行 / 展开区文本
// ═══════════════════════════════════════════

/**
 * 折叠行单行总述。
 * @param {array} intents 段意图数组
 * @param {boolean} truncated 参数是否被截断
 * @returns {string} 单行文本
 */
function cmdBrief(intents, truncated) {
    var unknown = 0;
    for (var i = 0; i < intents.length; i = i + 1) {
        if (!intents[i].known) {
            unknown = unknown + 1;
        }
    }
    var head = intents[0].known ? intents[0].text : '未识别命令';
    var body = (intents.length > 1) ? (head + ' 等 ' + intents.length + ' 段') : head;
    if (unknown > 0 && intents.length > 1) {
        body = body + '（' + unknown + ' 段未识别）';
    }
    if (truncated) {
        body = body + '（截断）';
    }
    return body;
}

/**
 * 展开区逐段对照文本（纯文本——调用方 textContent 落 DOM）。
 * @param {array} intents 段意图数组
 * @param {boolean} truncated 参数是否被截断
 * @returns {string} 多行文本
 */
function cmdDetail(intents, truncated) {
    var lines = [];
    lines.push('🔎 命令意图（' + intents.length + ' 段）');
    for (var i = 0; i < intents.length; i = i + 1) {
        var it = intents[i];
        var mark = it.piped ? ' ⤷' : '';
        if (it.known) {
            lines.push(' ' + (i + 1) + '.' + mark + ' ' + it.text);
        } else {
            lines.push(' ' + (i + 1) + '.' + mark + ' ❓ 未识别：' + it.text);
        }
    }
    if (truncated) {
        lines.push('（参数被宿主截断，仅前 200 字可见；完整命令见落盘历史）');
    }
    return lines.join('\n');
}

// ═══════════════════════════════════════════
// 覆盖率采集——未识别段登记与上报（后端 Data/cmd-unknown.json 持久化）
// 定位：CMD_RULES 未命中的命令段按 token 归并上报，后端聚合计数；定期查看后补规则，补完 clear 核销
// 纪律：渲染路径零阻塞（异步 fire-and-forget）；本页去重（同 token 只报一次）；上报失败不打扰渲染
// ═══════════════════════════════════════════

/** 上报端点——后端 AdminService.CmdUnknown 分区 */
var CMD_UNKNOWN_ENDPOINT = '/api/v1/cmd-unknown';

/** 样本截断长度——与后端 CmdUnknownSampleMax 同口径 */
var CMD_UNKNOWN_SAMPLE_MAX = 200;

/** 本页已上报 token——同 token 只报一次（刷新页面后重新计数，后端按次数聚合） */
var CMD_UNKNOWN_REPORTED = {};

/**
 * 段原文 → 首词原文（路径型取末段文件名；保留原始大小写——表里 raw 字段用）。
 * @param {string} text 段原文
 * @returns {string} 首词（空串=无法提取）
 */
function cmdUnknownRaw(text) {
    var t = (text || '').replace(/^\s+/, '');
    var m = /^("[^"]*"|'[^']*'|[^\s]+)/.exec(t);
    if (!m) { return ''; }
    var first = m[1].replace(/^["']|["']$/g, '');
    var i = Math.max(first.lastIndexOf('/'), first.lastIndexOf('\\'));
    if (i >= 0 && i + 1 < first.length) { first = first.substring(i + 1); }
    return first;
}

/**
 * 段原文 → 聚合 token（cmdUnknownRaw 的小写归一——后端按它聚合）。
 * @param {string} text 段原文
 * @returns {string} token（空串=无法提取）
 */
function cmdUnknownToken(text) {
    return cmdUnknownRaw(text).toLowerCase();
}

/**
 * 段意图数组 → 待上报项（仅未识别段；段内 token 去重）。
 * @param {array} intents 段意图数组
 * @returns {array} [{token, sample}]
 */
function cmdUnknownItems(intents) {
    var items = [];
    if (!intents) { return items; }
    var seen = {};
    for (var i = 0; i < intents.length; i = i + 1) {
        if (intents[i].known) { continue; }
        var token = cmdUnknownToken(intents[i].text);
        if (token.length === 0 || seen[token] === true) { continue; }
        seen[token] = true;
        var sample = intents[i].text || '';
        if (sample.length > CMD_UNKNOWN_SAMPLE_MAX) { sample = sample.substring(0, CMD_UNKNOWN_SAMPLE_MAX); }
        items.push({ token: token, raw: cmdUnknownRaw(intents[i].text), sample: sample });
    }
    return items;
}

/**
 * 上报未识别命令（异步 fire-and-forget）——本页去重后提交；上报失败释放标记（下次渲染可重试），渲染零阻塞。
 * @param {array} items 待上报项（cmdUnknownItems 产出）
 */
function cmdReportUnknown(items) {
    if (!items || items.length === 0 || typeof fetch !== 'function') { return; }
    var fresh = [];
    for (var i = 0; i < items.length; i = i + 1) {
        var token = items[i].token;
        if (CMD_UNKNOWN_REPORTED[token] === true) { continue; }
        CMD_UNKNOWN_REPORTED[token] = true;
        fresh.push(items[i]);
    }
    if (fresh.length === 0) { return; }
    try {
        fetch(CMD_UNKNOWN_ENDPOINT, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ items: fresh })
        }).then(function (resp) {
            // 失败（非 2xx）→ 释放标记：下次渲染仍可上报（不静默丢弃采集机会）
            if (!resp || resp.ok !== true) { cmdUnknownRelease(fresh); }
        }).catch(function () {
            cmdUnknownRelease(fresh);
        });
    } catch (e) {
        // 同步异常（fetch 不可用等）——释放标记 + 零阻塞（渲染不受影响）
        cmdUnknownRelease(fresh);
    }
}

/**
 * 释放上报标记——上报失败时调用（本次页面会话内下次渲染仍可上报）。
 * @param {array} items 已标记的待上报项
 */
function cmdUnknownRelease(items) {
    for (var i = 0; i < items.length; i = i + 1) {
        delete CMD_UNKNOWN_REPORTED[items[i].token];
    }
}

// ═══════════════════════════════════════════
// 规则表——命令 → 中文动作（顺序即优先级：专用 CLI 先于通用词）
// ═══════════════════════════════════════════

/** 通用「动词 + 首个目标参数」格式化器（目标取最后一个捕获组——单元格/双组规则统一） */
function cmdVerb(verb) {
    return function (seg, m) {
        return verb + cmdTarget(m[m.length - 1] || '');
    };
}

/** 无目标参数的动词格式化器（管道处理类——参数细节见原文块，避免误读开关值） */
function cmdVerbOnly(verb) {
    return function (seg, m) {
        return verb;
    };
}

/** 首个非开关参数（引号感知）→ 「 xxx」；无则空串 */
function cmdTarget(rest) {
    var args = cmdArgs(rest || '');
    if (args.length === 0) {
        return '';
    }
    return ' ' + cmdQuote(cmdTrunc(args[0], 60));
}

/** 分词——引号感知，剔除开关与连接符 */
function cmdArgs(rest) {
    var out = [];
    var re = /"([^"]*)"|'([^']*)'|(\S+)/g;
    var m;
    while ((m = re.exec(rest)) !== null) {
        var v = (m[1] !== undefined) ? m[1] : ((m[2] !== undefined) ? m[2] : m[3]);
        if (!v || v.length === 0) {
            continue;
        }
        if (v.charAt(0) === '-') {
            continue;
        }
        if (v === '|' || v === '>' || v === '>>' || v === ';' || v === '&&' || v === '||') {
            continue;
        }
        out.push(v);
    }
    return out;
}

var CMD_RULES = [
    // ── 宿主 / 基座 CLI（子指令解码——高价值可见面）──
    { re: /(^|[\/\\])cathome4\.exe\b\s*(.*)$/i, fmt: cmdFmtHostExe },
    { re: /(^|[\/\\])setup\.exe\b\s*(.*)$/i, fmt: cmdFmtSetUp },
    { re: /\bmau(\.exe)?\s+(\S+)/i, fmt: cmdFmtMauCli },
    // ── 编译链 / 版本控制 / Node ──
    { re: /^dotnet\s+(build|test|publish|run|restore|clean|pack)\b\s*(.*)$/i, fmt: cmdFmtDotnet },
    { re: /^git(\.exe)?\s+(\S+)\s*(.*)$/i, fmt: cmdFmtGit },
    { re: /^(npm|npx|node)\b\s*(.*)$/i, fmt: cmdVerb('运行 Node 工具链') },
    // ── 文件系统 cmdlet（含常用别名）──
    { re: /^(Get-ChildItem|gci|ls|dir)\b\s*(.*)$/i, fmt: cmdVerb('列出目录') },
    { re: /^(Get-Content|gc|cat|type)\b\s*(.*)$/i, fmt: cmdVerb('读取文件内容') },
    { re: /^(Select-String|findstr)\b\s*(.*)$/i, fmt: cmdVerb('搜索文件内容') },
    { re: /^(Test-Path)\b\s*(.*)$/i, fmt: cmdVerb('检查路径是否存在') },
    { re: /^Test-NetConnection\b\s*(.*)$/i, fmt: cmdVerb('测试网络连通性') },
    { re: /^(Get-Item|Get-ItemProperty)\b\s*(.*)$/i, fmt: cmdVerb('查看项属性') },
    { re: /^(Get-Process|gps)\b\s*(.*)$/i, fmt: cmdVerb('查看进程') },
    { re: /^(Stop-Process|taskkill)\b\s*(.*)$/i, fmt: cmdVerb('结束进程') },
    { re: /^(Copy-Item|cp|copy|xcopy|robocopy)\b\s*(.*)$/i, fmt: cmdVerb('复制文件') },
    { re: /^(Move-Item|mv|move)\b\s*(.*)$/i, fmt: cmdVerb('移动文件') },
    { re: /^(Remove-Item|rm|del|erase)\b\s*(.*)$/i, fmt: cmdVerb('删除文件') },
    { re: /^(New-Item|mkdir|md)\b\s*(.*)$/i, fmt: cmdVerb('新建项') },
    { re: /^(Set-Content|Add-Content|Out-File|Clear-Content|Tee-Object)\b\s*(.*)$/i, fmt: cmdVerb('写文件（会被拦截）') },
    // ── 管道处理 / 输出格式化 ──
    { re: /^Select-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('取字段/条数') },
    { re: /^Where-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('按条件筛选') },
    { re: /^Sort-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('排序') },
    { re: /^Group-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('分组计数') },
    { re: /^Measure-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('统计数值') },
    { re: /^ForEach-Object\b\s*(.*)$/i, fmt: cmdVerbOnly('逐项处理') },
    { re: /^(Format-Table|Format-List|Out-String|Out-Host|ConvertTo-Json|ConvertFrom-Json)\b\s*(.*)$/i, fmt: cmdVerbOnly('格式化输出') },
    // ── 网络 / 其他 ──
    { re: /^(curl|Invoke-WebRequest|Invoke-RestMethod|iwr|irm)\b\s*(.*)$/i, fmt: cmdVerb('发起 HTTP 请求') },
    { re: /^(netstat|Get-NetTCPConnection)\b\s*(.*)$/i, fmt: cmdVerb('查看网络连接') },
    { re: /^(Start-Sleep|sleep)\b\s*(.*)$/i, fmt: cmdVerb('等待') },
    { re: /^Get-Date\b\s*(.*)$/i, fmt: cmdVerb('获取当前时间') },
    { re: /^(Set-Location|cd|chdir)\b\s*(.*)$/i, fmt: cmdVerb('切换目录') }
];

// ═══════════════════════════════════════════
// 专用格式化器
// ═══════════════════════════════════════════

/** dotnet 子命令 → 中文动词 + 目标项目 */
function cmdFmtDotnet(seg, m) {
    var act = m[1].toLowerCase();
    var map = {
        build: '编译 C# 项目',
        test: '运行 C# 测试',
        publish: '发布 C# 项目',
        run: '运行 C# 项目',
        restore: '还原 NuGet 依赖',
        clean: '清理 C# 构建产物',
        pack: '打包 NuGet 包'
    };
    return (map[act] || ('dotnet ' + act)) + cmdTarget(m[2] || '');
}

/** git 子命令 → 中文动作 */
function cmdFmtGit(seg, m) {
    // 带值开关（-C <repo> 等）先行——动作在开关之后
    var mm = /^git(?:\.exe)?\s+(?:-[A-Za-z\-]+\s+\S+\s+)*(\S+)\s*(.*)$/i.exec(seg);
    var act = mm ? (mm[1] || '').toLowerCase() : '';
    var rest = mm ? (mm[2] || '') : '';
    var map = {
        status: '查看仓库状态',
        log: '查看提交历史',
        diff: '查看改动差异',
        show: '查看提交内容',
        branch: '查看/操作分支',
        checkout: '切换分支',
        commit: '提交改动',
        push: '推送远端',
        pull: '拉取远端',
        fetch: '抓取远端更新',
        add: '暂存改动',
        stash: '暂存工作区',
        clone: '克隆仓库',
        rev_parse: '解析版本引用',
        'rev-parse': '解析版本引用'
    };
    var head = map[act] || ('git ' + act);
    return head + cmdTarget(rest);
}

/** 宿主 CLI——--run 子指令解码（部署/重载类命令可见性关键面） */
function cmdFmtHostExe(seg, m) {
    var rest = m[2] || '';
    var run = /--run\s+["']?([^"']*)/i.exec(rest);
    if (run) {
        return '宿主 CLI 执行指令「' + cmdHostCommand(cmdTrim(run[1])) + '」';
    }
    var tc = /--tool-check\s+(\S+)/i.exec(rest);
    if (tc) {
        return '工具全链自检 ' + cmdQuote(tc[1]);
    }
    var script = /--script\s+(\S+)/i.exec(rest);
    if (script) {
        return '批量指令脚本 ' + cmdQuote(cmdTrunc(script[1], 60));
    }
    if (/--selfcheck/i.test(rest)) {
        return '宿主启动自检';
    }
    if (/--probe-llm/i.test(rest)) {
        return 'LLM 返回体探针';
    }
    if (/--majordomopush/i.test(rest)) {
        return '宿主启动·回执注入';
    }
    return '调用宿主（' + cmdTrunc(cmdTrim(rest), 50) + '）';
}

/** --run 子指令 → 中文（前缀表驱动） */
function cmdHostCommand(cmd) {
    var table = [
        { p: 'reload', v: '热重载 Flow ' },
        { p: 'session count', v: '查询会话数' },
        { p: 'session clear', v: '清空全部会话' },
        { p: 'session.rollback', v: '回滚会话' },
        { p: 'session.fork', v: '分支新会话' },
        { p: 'note.start', v: '启动 Note 计划' },
        { p: 'note.add', v: '推进 Note 步骤' },
        { p: 'freeze', v: '冻结宿主轮转' },
        { p: 'status', v: '查询宿主状态' },
        { p: 'restart', v: '重启（宿主内）' },
        { p: 'kill', v: '紧急停止' },
        { p: 'spawn', v: '创建实体' },
        { p: 'load', v: '加载实体' },
        { p: 'unload', v: '卸载实体' },
        { p: 'params', v: '查询参数' },
        { p: 'param', v: '设置参数' },
        { p: 'boards', v: '查询面板' }
    ];
    var low = cmd.toLowerCase();
    for (var i = 0; i < table.length; i = i + 1) {
        if (low.indexOf(table[i].p) === 0) {
            var rest = cmdTrim(cmd.substring(table[i].p.length));
            if (table[i].v.charAt(table[i].v.length - 1) === ' ') {
                return table[i].v + cmdTrunc(rest, 40);
            }
            return table[i].v + (rest.length > 0 ? '（' + cmdTrunc(rest, 40) + '）' : '');
        }
    }
    return cmdTrunc(cmd, 50);
}

/** SetUp 一键链 */
function cmdFmtSetUp(seg, m) {
    var rest = m[2] || '';
    if (/relaunch/i.test(rest)) {
        return '一键部署链·重启接力（relaunch）';
    }
    if (/deploy/i.test(rest)) {
        return '一键部署链·发布到目标目录（deploy）';
    }
    if (/prepare/i.test(rest)) {
        return '一键部署链·就地自举（prepare）';
    }
    return '一键部署链（SetUp）';
}

/** Mau 基座 CLI */
function cmdFmtMauCli(seg, m) {
    var all = cmdArgs(seg);
    var act = (all[1] || '').toLowerCase();
    var map = {
        verify: 'Mau 语料验证',
        gen: 'Mau 语料生成',
        proj: 'Mau 组翻译+编译',
        test: 'Mau 全链门禁测试',
        check: 'Mau 门禁检查',
        bricks: 'Mau 积木索引',
        debug: 'Mau 单步调试'
    };
    var head = map[act] || ('Mau 执行 ' + act);
    var target = (all.length > 2) ? (' ' + cmdQuote(cmdTrunc(all[2], 60))) : '';
    return head + target;
}

// ═══════════════════════════════════════════
// 小工具
// ═══════════════════════════════════════════

/** 去首尾空白 */
function cmdTrim(s) {
    return (s || '').replace(/^\s+|\s+$/g, '');
}

/** 截断 + 省略号 */
function cmdTrunc(s, n) {
    var t = s || '';
    if (t.length <= n) {
        return t;
    }
    return t.substring(0, n) + '…';
}

/** 中文引号包裹 */
function cmdQuote(s) {
    return '「' + (s || '') + '」';
}
