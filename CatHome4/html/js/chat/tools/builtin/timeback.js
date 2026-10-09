// ═══════════════════════════════════════════
// chat/tools/builtin/timeback.js —— timeback-start / timeback-back · 上下文作用域（内置）
// 声明：图标 ⚓（开锚）/ ♻️（回收）+ 折叠行读结构化头；
//       输出段**专属解析**——开锚：状态行（单行）——规范与骨架由「域规范卡」（systemauto user）承载；
//                            回收：节约读数（条数 / token / 工具）+ 宿主台账段（⚙）+ findings 四段（emoji 分段）。
// 节约口径：条数与 token 取自结构头（宿主实测）——`released`（释放条数）· `grew`（域内净增 token
//          = 开锚时取值与回收时取值之差，随域回收销毁）· `tools`（域内工具使用条数——全量台账，含只读/被拒）。
// 拆分（2026-10-09）：原单工具 timeback（action=start|back）→ 起点 / 回收两个工具名，功能不变。
// ═══════════════════════════════════════════

/// findings 段 emoji 表——段标签 → 前缀图标（不改骨架结构，只加可扫读前缀）
/// 覆盖两套骨架：审查类（成果 / 未竟 / 卡点 / 失败）· 实现类（变更 / 跑测 / 未竟 / 卡点与解法 / 失败）。
var TB_SEG_ICONS = { '成果': '📌', '变更': '📝', '跑测': '🧪', '未竟': '🔍', '卡点': '⚠️', '卡点与解法': '⚠️', '失败': '❌' };

/// 台账段首行标识——宿主工具台账（`[本域工具台账 · 宿主记录 · N 条 · 含只读与被拒]`）
var TB_WRITES_CAP = '[本域工具台账';

/// 正文切分——台账段（宿主事实）与其余正文分离；无台账段时 writes 为空数组
function tbSplitWrites(body) {
    var lines = body.replace(/\r/g, '').split('\n');
    var writes = [];
    var rest = [];
    var inWrites = false;
    for (var i = 0; i < lines.length; i = i + 1) {
        var t = lines[i];
        if (!inWrites && t.indexOf(TB_WRITES_CAP) === 0) {
            inWrites = true;
            writes.push(t);
            continue;
        }
        if (inWrites) {
            if (t.length === 0) { inWrites = false; continue; }
            writes.push(t);
            continue;
        }
        rest.push(t);
    }
    return { writes: writes, rest: rest };
}

/// findings 正文行渲染——四段标签行加 emoji 前缀；续行缩进；空行丢弃；其余行原样保留
function tbFindingsLines(lines) {
    var out = [];
    for (var i = 0; i < lines.length; i = i + 1) {
        var t = lines[i];
        if (t.length === 0) { continue; }
        var m = /^\s*(变更|跑测|卡点与解法|成果|未竟|卡点|失败)：/.exec(t);
        if (m) {
            out.push(TB_SEG_ICONS[m[1]] + ' ' + t.replace(/^\s+/, ''));
            continue;
        }
        out.push(t);
    }
    return out;
}

/// 开锚卡——状态行（结构头）单行；规范与骨架迁入「域规范卡」（systemauto user），本层不再承载
toolDecl('timeback-start', {
    icon: '⚓',
    inputLines: function (a) {
        return ['⚓ 开锚' + ((a.purpose) ? (' · ' + ovPeek(a.purpose)) : ' · （缺用途）')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '⚓ 开锚' + ((a.purpose) ? (' · ' + ovPeek(a.purpose)) : '') + ovStat(r);
        }
        var m = h.meta;
        var type = ovMetaStr(m, 'type');
        var typeTail = (type.length > 0) ? (' · ' + type) : '';
        var purpose = ovMetaStr(m, 'purpose');
        var purposeTail = (purpose.length > 0) ? (' · ' + ovPeek(purpose)) : '';
        return '⚓ TimeBack #' + ovMetaNum(m, 'id', 0) + ' 已开锚' + typeTail + ' · 锚点 ' + ovMetaNum(m, 'anchor', 0) + purposeTail;
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var m = h.meta;
        var type = ovMetaStr(m, 'type');
        var typeTail = (type.length > 0) ? (' · 域类型「' + type + '」') : '';
        var purpose = ovMetaStr(m, 'purpose');
        var out = [];
        out.push('⚓ 作用域 #' + ovMetaNum(m, 'id', 0) + typeTail
            + ((purpose.length > 0) ? (' · 用途「' + purpose + '」') : ''));
        return out;
    }
});

/// 回收卡——节约读数（结构头：条数 / token / 工具）+ 宿主台账段（⚙）+ findings 四段
toolDecl('timeback-back', {
    icon: '♻️',
    inputLines: function (a) {
        return ['♻️ 回收', '载荷 ' + ovPeek(a.findings)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '♻️ 回收' + ovStat(r);
        }
        var m = h.meta;
        var type = ovMetaStr(m, 'type');
        var typeTail = (type.length > 0) ? (' · ' + type) : '';
        var tools = ovMetaNum(m, 'tools', -1);
        var w = (tools > 0) ? (' · 工具 ' + tools + ' 次') : '';
        var purpose = ovMetaStr(m, 'purpose');
        var purposeTail = (purpose.length > 0) ? (' · ' + ovPeek(purpose)) : '';
        return '♻️ TimeBack #' + ovMetaNum(m, 'id', 0) + ' 已回收' + typeTail + ' · 锚点 ' + ovMetaNum(m, 'anchor', 0) + w + purposeTail;
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var m = h.meta;
        var type = ovMetaStr(m, 'type');
        var typeTail = (type.length > 0) ? (' · 域类型「' + type + '」') : '';
        var purpose = ovMetaStr(m, 'purpose');
        var out = [];
        out.push('♻️ 作用域 #' + ovMetaNum(m, 'id', 0) + typeTail + ' · 锚点 ' + ovMetaNum(m, 'anchor', 0)
            + ' 已回收' + ((purpose.length > 0) ? (' · 用途「' + purpose + '」') : ''));
        // 情报行（`📉 释放 … 域内节约 …token · 工具使用 … 次`）由**返回体正文自带**——本层不自造，避免与正文重复；
        // 本层只做分段与 emoji（正文行原样透传）
        var cut = tbSplitWrites(h.body);
        if (cut.writes.length > 0) {
            out.push('⚙ ' + cut.writes[0]);
            for (var wi = 1; wi < cut.writes.length; wi = wi + 1) {
                out.push('   ' + cut.writes[wi]);
            }
        }
        var findings = tbFindingsLines(cut.rest);
        for (var i = 0; i < findings.length; i = i + 1) {
            out.push(findings[i]);
        }
        return out;
    }
});
