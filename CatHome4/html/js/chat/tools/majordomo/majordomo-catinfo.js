// ═══════════════════════════════════════════
// chat/tools/majordomo/majordomo-catinfo.js —— 全猫状态统计（专属骨架 catinfo）
//
// 定位：全猫状态统计的**专属骨架渲染**——返回体 = 整块分类 JSON；前端摊平为「每猫一行」键值行。
// 口径（莎定 2026-09-24）：与 info 同规格——后端整块分类 JSON（缩进 / 中文直显），前端摊平为键值行；
//       缺字段不显示该行（后端「无则省略键」约定——前端不补空行、不写 NaN）。
// 来源：重构前 `chat-tools.js::chatSkelCatInfo / chatCatInfoPairs / chatAgoText`（A200 恢复）。
// ═══════════════════════════════════════════

/// catinfo 骨架——整块分类 JSON → 每猫一行键值（解析不出 → 原文兜底）
function skelCatInfo(tool) {
    var segs = [segInput(tool, '')];
    var d = tryJson(tool.result);
    if (!d) {
        segs.push(segOutput(tool, '输出', function (body, text, isErr) {
            segBlock(body, isErr ? 'tr err' : 'tr', text);
        }, tool.result));
        return { tag: '', tagCls: '', segs: segs };
    }
    var pairs = ovCatInfoPairs(d);
    segs.push(segOutput(tool, '输出 · ' + pairs.length + ' 只猫', function (body, text, isErr) {
        segKv(body, pairs);
    }, ovInfoPeek(pairs)));
    return { tag: '', tagCls: '', segs: segs };
}

/// 全猫状态块 → 键值行（每猫一行：名(id)★ · 运行态:端口 · 相位/流式态 · 前文 · 活跃 · 轮次 · 消息 · 待处理 · Note）
function ovCatInfoPairs(d) {
    var pairs = [];
    var cats = d.cats;
    if (!cats || !cats.length) {
        return pairs;
    }
    for (var i = 0; i < cats.length; i = i + 1) {
        var c = cats[i];
        var name = field(c, 'name');
        var id = field(c, 'id');
        var head = name;
        if (id.length > 0 && id !== name) {
            head = name + '(' + id + ')';
        }
        if (c.special === true) {
            head = head + '★';
        }
        var parts = [];
        var port = fieldNum(c, 'port');
        if (c.running === true) {
            parts.push(port.length > 0 && port !== '0' ? '运行中 :' + port : '运行中');
        } else {
            parts.push('静默');
        }
        var phase = field(c, 'phase');
        var runState = field(c, 'runState');
        if (phase.length > 0) {
            // 相位（四相环）+ 流式态（七态）——空闲态不重复标注（idle 与 Idle 同义）
            parts.push(runState.length > 0 && runState !== 'idle' ? phase + '/' + runState : phase);
        }
        var ctx = fieldNum(c, 'context');
        var ctxCount = fieldNum(c, 'contextCount');
        if (ctx.length > 0) {
            parts.push('前文 ' + ctx + ' tokens' + (ctxCount.length > 0 ? ' / ' + ctxCount + ' 条' : ''));
        }
        var last = fieldNum(c, 'lastActiveAt');
        var ago = ovAgoText(last);
        if (ago.length > 0) {
            parts.push('活跃 ' + ago);
        }
        var round = fieldNum(c, 'round');
        if (round.length > 0) {
            parts.push('轮 ' + round);
        }
        var msgs = fieldNum(c, 'msgCount');
        if (msgs.length > 0) {
            parts.push('消息 ' + msgs);
        }
        var pending = fieldNum(c, 'pending');
        if (pending.length > 0 && pending !== '0') {
            parts.push('待处理 ' + pending);
        }
        if (c.noteActive === true) {
            parts.push('Note 激活');
        }
        pairs.push({ k: head, v: parts.join(' · ') });
    }
    return pairs;
}

/// 距今文本——Unix 毫秒 → 「N 秒/分钟/小时/天前」（无效值返回空串，该段不显示）
function ovAgoText(ms) {
    var t = Number(ms);
    if (!isFinite(t) || t <= 0) {
        return '';
    }
    var diff = Date.now() - t;
    if (diff < 0) {
        diff = 0;
    }
    var sec = Math.floor(diff / 1000);
    if (sec < 60) {
        return sec + ' 秒前';
    }
    var min = Math.floor(sec / 60);
    if (min < 60) {
        return min + ' 分钟前';
    }
    var hour = Math.floor(min / 60);
    if (hour < 24) {
        return hour + ' 小时前';
    }
    return Math.floor(hour / 24) + ' 天前';
}

// ── 声明（覆盖层：折叠行文案；段结构归专属骨架）──────────────

toolDecl('majordomo-catinfo', {
    inputLines: function () {
        return ['查看全猫状态统计'];
    },
    headline: function (a, r) {
        // 返回体 = 分类 JSON 块（无「头 + 正文」两段）——整块解析，不用 metaHead
        var d = tryJson(r);
        if (!d) {
            return '全猫状态' + ovStat(r);
        }
        var n = fieldNum(d, 'count');
        if (n.length === 0) {
            return '全猫状态';
        }
        return '全猫状态 · ' + n + ' 只猫';
    }
});
