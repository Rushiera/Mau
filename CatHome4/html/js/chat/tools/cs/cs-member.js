// ═══════════════════════════════════════════
// chat/tools/cs/cs-member.js —— cs-member / 成员增删改（CsCat）
// 声明：折叠行与输出段按 op 分支（insert 单 / insert 批量 codes / delete / rename）；
//       批量落盘逐条列行号区间（`#n Lx-y · kind`）。
// ═══════════════════════════════════════════

toolDecl('cs-member', {
    inputLines: function (a) {
        var op = a.op || '';
        if (op === 'insert') {
            if (a.codes && a.codes.length) {
                return ['批量插入 ' + a.codes.length + ' 个成员到 ' + ovText(a.class) + ' · 位置 ' + ovText(a.position)];
            }
            return ['插入成员到 ' + ovText(a.class) + ' · 位置 ' + ovText(a.position)];
        }
        if (op === 'delete') {
            return ['删除成员 ' + ovText(a.class) + '.' + ovText(a.member)];
        }
        if (op === 'rename') {
            return ['重命名 ' + ovText(a.class) + '.' + ovText(a.oldName) + ' → ' + ovText(a.newName)];
        }
        return ['成员操作 ' + ovText(a.class) + ' · op=' + ovText(a.op)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return '成员操作 ' + ovText(a.class) + ovStat(r);
        }
        var m = h.meta;
        if (m.op === 'insert') {
            if (m.entries && m.entries.length) {
                return '批量插入 ' + ovMetaNum(m, 'items', 0) + ' 个成员 · 落盘 L' + m.entries[0].start + '-' + m.entries[m.entries.length - 1].end;
            }
            return '插入 ' + ovMetaStr(m, 'target') + ' · 落盘 L' + ovMetaNum(m, 'start', 0) + '-' + ovMetaNum(m, 'end', 0);
        }
        if (m.op === 'delete') {
            return '删除 ' + ovMetaStr(m, 'target') + '.' + ovMetaStr(m, 'member');
        }
        if (m.op === 'rename') {
            return '重命名 ' + ovMetaStr(m, 'oldName') + ' → ' + ovMetaStr(m, 'newName') + ' · ' + ovMetaNum(m, 'items', 0) + ' 文件';
        }
        return '成员操作 ' + ovText(a.class);
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var m = h.meta;
        if (m.op === 'insert') {
            if (m.entries && m.entries.length) {
                var bulk = [];
                for (var bi = 0; bi < m.entries.length; bi = bi + 1) {
                    bulk.push('#' + (bi + 1) + ' L' + m.entries[bi].start + '-' + m.entries[bi].end + ' · ' + (m.entries[bi].kind || ''));
                }
                return bulk;
            }
            return ['已落盘 ' + ovMetaStr(m, 'file') + ' · L' + ovMetaNum(m, 'start', 0) + '-' + ovMetaNum(m, 'end', 0) + ' · ' + ovMetaStr(m, 'kind')];
        }
        if (m.op === 'delete') {
            return ['已删除 ' + ovMetaStr(m, 'target') + '.' + ovMetaStr(m, 'member') + '（' + ovMetaStr(m, 'file') + '）'];
        }
        if (m.op === 'rename') {
            return ['已重命名 ' + ovMetaStr(m, 'oldName') + ' → ' + ovMetaStr(m, 'newName') + ' · ' + ovMetaNum(m, 'items', 0) + ' 文件'];
        }
        return ['（已应用）'];
    }
});
