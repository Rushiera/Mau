// ═══════════════════════════════════════════
// chat/tools/py/py-run.js —— py-run / 执行 py 脚本（PyCat）
// 声明：图标 🐍 + 输入意图行（脚本 / 参数）+ 折叠行与输出自然语言化；段结构归骨架 `json`。
// 回执形态（PyService.Run）：{exit, timeout, stdout, stderr, stdoutLines, stderrLines, elapsedMs, script}
// ═══════════════════════════════════════════

toolDecl('py-run', {
    icon: '🐍',
    inputLines: function (a) {
        var args = (typeof a.args === 'string') ? a.args : '';
        var tail = (args.length > 0) ? (' ' + args) : '';
        var to = Number(a.timeout_ms);
        var toTail = (to > 0) ? (' · 超时 ' + (to / 1000).toFixed(0) + 's') : '';
        return ['运行 py 脚本 ' + ovText(a.script) + tail + toTail];
    },
    headline: function (a, r) {
        var head = '运行 ' + ovShort(a.script);
        if (r === undefined) {
            return head + ' · 执行中';
        }
        var o = ovJson(r);
        if (!o) {
            return head + ovStat(r);
        }
        if (o.timeout === true) {
            return head + ' · 超时终止';
        }
        return head + ' · exit ' + ovMetaNum(o, 'exit', -1);
    },
    badge: function (r) {
        var o = ovJson(r);
        if (!o) {
            return '';
        }
        if (o.timeout === true) {
            return ' · 超时';
        }
        return ' · exit ' + ovMetaNum(o, 'exit', -1);
    },
    outputLines: function (r, a, isErr) {
        if (isErr) {
            return null;
        }
        if (r === undefined) {
            return ['执行中…'];
        }
        var o = ovJson(r);
        if (!o) {
            return null;
        }
        var so = ovMetaStr(o, 'stdout').replace(/\n+$/, '');
        var se = ovMetaStr(o, 'stderr').replace(/\n+$/, '');
        var soArr = (so.length > 0) ? so.split('\n') : [];
        var seArr = (se.length > 0) ? se.split('\n') : [];
        var ms = ovMetaNum(o, 'elapsedMs', -1);
        var head = ((ms >= 0) ? ('耗时 ' + (ms / 1000).toFixed(1) + 's · ') : '')
            + 'stdout ' + soArr.length + ' 行 / stderr ' + seArr.length + ' 行';
        if (o.timeout === true) {
            head = head + '（超时终止）';
        }
        var out = [head];
        var i;
        for (i = 0; i < soArr.length; i = i + 1) {
            out.push(soArr[i]);
        }
        for (i = 0; i < seArr.length; i = i + 1) {
            out.push('stderr │ ' + seArr[i]);
        }
        return out;
    }
});
