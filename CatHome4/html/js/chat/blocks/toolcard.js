// ═══════════════════════════════════════════
// blocks/toolcard.js —— toolcard 块（persist 类 · 完成态工具卡）
//
// 契约：
//   item = { type:'toolcard', ts, msgIndex, round,
//            payload:{ name, arguments, result, order?, toolIndex?, toolTotal?, images? } }
//   · result === undefined 表示进行中（新契约下由 live 面的 toolcard.pending 承载，persist 面恒为终态）
//   · name = 工具名；arguments = 参数 JSON 原文；result = 结果原文（可带结构化头）
//   · order = 执行序档位（缺省不渲染徽标）；toolIndex / toolTotal = 批次内序号（并发批次显示 [icon n/m]）
//
// 产出：.chat-row.assistant.tool > details.chat-tool[.err]
//         └ summary.tn（图标 + 徽标 + 变体标签 + 折叠行文案 + 结果规模） + 段数组（lib/tool-format 骨架）
//
// 来源：chat-view.js chatToolCard（第 152-250 行）+ chatResultInfo / chatResultSuffix（第 41-72 行）
//
// 归属：
//   · 覆盖表体系（chatToolHeadline / chatToolFallbackHeadline / CHAT_TOOL_OVERRIDES）**不采用**——见 lib/tool-format.js 头注
//   · 命令解码显示 → fx/cmd-intent（独立功能面——A176 归位；本件只调用，不自持解码与显示构造）
//   · 展开态点击收起 → lib/press.js（bindPressToggle）
//   · DOM 挂载 / 滚动跟随 / 计时表（data-start / chatLiveMark）——时刻归主干注入
// ═══════════════════════════════════════════

function buildToolBlock(payload) {
    // 完成态工具卡——行容器 + 折叠卡（默认折叠）
    var row = blockRow('toolcard');
    row.appendChild(buildToolCard(payload, false));
    return row;
}

function resultInfo(resultText) {
    // 结果规模信息——ps 结果 JSON 解析（stdout/stderr 合计 + 截断/超时标记）；非 JSON 按纯文本长度
    // 动机：消耗可见才能优化——前端把规模与截断状态显式呈现
    var text = resultText || '';
    var info = { chars: text.length, truncated: false, timeout: false };
    if (text.length === 0) { return info; }
    var trimmed = text.replace(/^\s+|\s+$/g, '');
    if (trimmed.charAt(0) !== '{') { return info; }
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

function resultSuffix(info) {
    // 折叠行消耗标注——大结果（≥1000 字符）或截断/超时才追加（小结果不标，避免噪音）
    if (!info) { return ''; }
    if (info.truncated) {
        return ' · ⚠️ 已达上限 ' + fmtCount(info.chars) + ' 字符' + (info.timeout ? '（超时终止）' : '');
    }
    if (info.chars >= 1000) {
        return ' · ' + fmtCount(info.chars) + ' 字符';
    }
    return '';
}

function buildToolCard(tool, open) {
    // 工具卡——details 结构（open=true 展开：先行卡直接展示 ⏳；缺省折叠——点击 summary 展开/收起）
    // 失败态——ERR 前缀 或 结构化返回头 ok:false（lib/tool-format isErrResult 单一出口）
    var t = tool || {};
    var isErr = isErrResult(t.result);
    var det = el('details', 'chat-tool' + (isErr ? ' err' : ''));
    det.open = (open === true);
    var sum = el('summary', 'tn');

    // 工具前缀——并发批次显示 [icon n/m]；单次用类型图标
    var prefix;
    if (isErr) {
        prefix = (t.toolTotal > 1) ? ('[⚠️ ' + (t.toolIndex || '?') + '/' + t.toolTotal + '] ') : '⚠️ ';
    } else if (t.toolTotal > 1) {
        prefix = '[' + iconOf(t.name) + ' ' + (t.toolIndex || '?') + '/' + t.toolTotal + '] ';
    } else {
        prefix = iconOf(t.name) + ' ';
    }
    sum.appendChild(document.createTextNode(prefix));

    // 执行序徽标——宿主 order 表裁决；载荷缺 order 字段 = 旧块 / 未带——不渲染，零猜测
    if (t.order !== undefined && t.order !== null && String(t.order).length > 0) {
        sum.appendChild(elText('span', 'chat-order', '⚙' + t.order));
        sum.appendChild(document.createTextNode(' '));
    }

    // 骨架分派（lib/tool-format）——命中 → 段结构 + 工具变体标签；未命中 → 探测骨架
    var body = toolBody(t);
    var resultInfo2 = resultInfo(t.result);
    if (body && body.tag) {
        sum.appendChild(elText('span', 'ps-tag ' + (body.tagCls || ''), body.tag));
        sum.appendChild(document.createTextNode(' '));
    }

    // 折叠行文案——PS 双线：命令意图显示（fx/cmd-intent 独立功能件）→ 骨架中文名兜底 → 工具名
    // （逐工具自然语言 headline 覆盖层不采用；解码与显示构造归 fx 件，本件只调用）
    var cmdIntent = (typeof fxCmdIntentDecode === 'function') ? fxCmdIntentDecode(t.name, t.arguments) : null;
    var headline = cmdIntent ? cmdIntent.brief : (SKEL_LABELS[skeletonOf(t.name)] || t.name || '?');
    sum.appendChild(document.createTextNode(headline + (body ? resultSuffix(resultInfo2) : '')));
    det.appendChild(sum);
    if (cmdIntent && typeof fxCmdIntentAttach === 'function') {
        // 展开区首块——逐段意图对照（命令原文仍在输入段；未识别段标 ❓）
        fxCmdIntentAttach(det, cmdIntent);
    }

    if (body && body.segs) {
        // 骨架路径——输入 / 输出段（二级折叠；段内沿用 .ta / .tr / .ta.warn 锚点类）
        for (var si = 0; si < body.segs.length; si++) {
            det.appendChild(body.segs[si]);
        }
    } else {
        // 回落路径——参数与结果原文（零回归）
        if (t.arguments) {
            det.appendChild(elText('div', 'ta', t.arguments));
        }
        if (resultInfo2.truncated) {
            det.appendChild(elText('div', 'ta warn',
                '⚠️ 输出已达上限被截断——后续内容未回传' + (resultInfo2.timeout ? '；进程超时已终止' : '')));
        }
        if (t.result) {
            det.appendChild(elText('div', 'tr' + (isErr ? ' err' : ''), t.result));
        } else if (t.result === undefined) {
            det.appendChild(elText('div', 'ta ' + PENDING_HOLD_CLS, pendingHoldText(0)));
        }
    }

    // 展开态整块点击收起（折叠头仍走原生 toggle）
    bindPressToggle(det, function (d) { d.open = false; }, { onlyWhenOpen: true });
    return det;
}
