// ═══════════════════════════════════════════
// chat/tools/py/py-service.js —— py-service / py 服务启停与状态（PyCat）
// 声明：图标 🔌 + 输入意图行（动作 / 服务名）+ 折叠行与输出自然语言化；段结构归骨架 `json`。
// 回执形态（PyService.ServiceJson）：{name, state(running/stopped), alive, note}
// ═══════════════════════════════════════════

/// 服务态码 → 中文（未知码原样显示，不猜）
function pyStateText(state) {
    if (state === 'running') {
        return '运行中';
    }
    if (state === 'stopped') {
        return '已停止';
    }
    return state;
}

toolDecl('py-service', {
    icon: '🐍',
    inputLines: function (a) {
        return ['py 服务 ' + ovText(a.action) + ' · ' + ovText(a.name)];
    },
    headline: function (a, r) {
        var head = 'py 服务 ' + ovText(a.action) + ' ' + ovShort(a.name);
        if (r === undefined) {
            return head + ' · 处理中';
        }
        var o = ovJson(r);
        if (!o) {
            return head + ovStat(r);
        }
        return head + ' · ' + pyStateText(ovMetaStr(o, 'state'));
    },
    badge: function (r) {
        var o = ovJson(r);
        if (!o) {
            return '';
        }
        return ' · ' + pyStateText(ovMetaStr(o, 'state'));
    },
    outputLines: function (r, a, isErr) {
        if (isErr) {
            return null;
        }
        if (r === undefined) {
            return ['处理中…'];
        }
        var o = ovJson(r);
        if (!o) {
            return null;
        }
        var out = ['服务 ' + ovMetaStr(o, 'name') + '：' + pyStateText(ovMetaStr(o, 'state'))
            + (o.alive === true ? '（进程存活）' : '')];
        var note = ovMetaStr(o, 'note');
        if (note.length > 0) {
            out.push(note);
        }
        return out;
    }
});
