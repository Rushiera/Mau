// ═══════════════════════════════════════════
// blocks/toolcard.js —— toolcard 块（persist 类 · 完成态工具卡）
//
// 契约：
//   item = { type:'toolcard', ts, msgIndex, round,
//            payload:{ name, arguments, result, images? } }
//   · name = 工具名；arguments = 参数 JSON 原文；result = 结果原文（可带结构化头）
//   · 计数类字段（toolIndex / toolTotal / order / durMs / 字符规模）不进渲染——规模信息归后端
//
// 产出：.chat-row.assistant.tool > details.chat-tool[.err]
//         └ summary.tn（图标 + 变体标签 + 折叠行文案） + 段数组（lib/tool-format 骨架）
//
// 归属：
//   · 命令解码显示 → fx/cmd-intent（独立功能面——本件只调用，不自持解码与显示构造）
//   · 展开态点击收起 → lib/press.js（bindPressToggle）
//   · DOM 挂载 / 滚动跟随归主干
// ═══════════════════════════════════════════

function buildToolBlock(payload, type) {
    // 工具卡——行容器 + 折叠卡（默认折叠）
    // type 可指定（缺省 toolcard）——live 面以 'toolrun' 复用本渲染件（两区同源，行身份随来源）
    var row = blockRow(type || 'toolcard');
    row.appendChild(buildToolCard(payload, false));
    return row;
}

function resultInfo(resultText) {
    // 结果边界信息——仅截断 / 超时（规模计数归后端；此处只作警示判据）
    var info = { truncated: false, timeout: false };
    var text = resultText || '';
    if (text.length === 0) { return info; }
    var trimmed = text.replace(/^\s+|\s+$/g, '');
    if (trimmed.charAt(0) !== '{') { return info; }
    try {
        var o = JSON.parse(trimmed);
        if (o && typeof o.stdout === 'string') {
            info.truncated = (o.truncated === true);
            info.timeout = (o.timeout === true);
        }
    } catch (e) {
        // 非 JSON——无边界信息
    }
    return info;
}

function buildToolCard(tool, open) {
    // 工具卡——details 结构（open=true 展开：进行中卡直接展示；缺省折叠——点击 summary 展开/收起）
    // 失败态——ERR 前缀 或 结构化返回头 ok:false（lib/tool-format isErrResult 单一出口）
    var t = tool || {};
    var isErr = isErrResult(t.result);
    var det = el('details', 'chat-tool' + (isErr ? ' err' : ''));
    det.open = (open === true);
    var sum = el('summary', 'tn');

    // 折叠行前缀——失败态警示；其余按工具类型图标（无批次编号、无执行序徽标）
    sum.appendChild(document.createTextNode((isErr ? '⚠️ ' : iconOf(t.name)) + ' '));

    // 骨架分派（lib/tool-format）——命中 → 段结构 + 工具变体标签；未命中 → 探测骨架
    var body = toolBody(t);
    if (body && body.tag) {
        sum.appendChild(elText('span', 'ps-tag ' + (body.tagCls || ''), body.tag));
        sum.appendChild(document.createTextNode(' '));
    }

    // 折叠行文案——PS 双线：命令意图显示（fx/cmd-intent 独立功能件）→ 骨架中文名兜底 → 工具名
    var cmdIntent = (typeof fxCmdIntentDecode === 'function') ? fxCmdIntentDecode(t.name, t.arguments) : null;
    var headline = cmdIntent ? cmdIntent.brief : (SKEL_LABELS[skeletonOf(t.name)] || t.name || '?');
    sum.appendChild(document.createTextNode(headline));
    det.appendChild(sum);
    if (cmdIntent && typeof fxCmdIntentAttach === 'function') {
        // 展开区首块——逐段意图对照（命令原文仍在输入段；未识别段标 ❓）
        fxCmdIntentAttach(det, cmdIntent);
    }

    var info = resultInfo(t.result);
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
        if (info.truncated) {
            det.appendChild(elText('div', 'ta warn',
                '⚠️ 输出已达上限被截断——后续内容未回传' + (info.timeout ? '；进程超时已终止' : '')));
        }
        if (t.result) {
            det.appendChild(elText('div', 'tr' + (isErr ? ' err' : ''), t.result));
        }
    }

    // 展开态整块点击收起（折叠头仍走原生 toggle）
    bindPressToggle(det, function (d) { d.open = false; }, { onlyWhenOpen: true });
    return det;
}
