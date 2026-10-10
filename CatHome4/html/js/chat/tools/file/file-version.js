// ═══════════════════════════════════════════
// chat/tools/file/file-version.js —— file-version / PE 版本三件读取（FileCat）
// 声明：折叠行取结果里的「版本:」行（前端零计数的解析出口）；段结构归骨架 `listing`。
// ═══════════════════════════════════════════

toolDecl('file-version', {
    inputLines: function (a) {
        return ['读取版本信息 ' + ovText(a.path)];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        var body = h ? h.body : r;
        var name = ovShort(a.path);
        var v = ovLineField(body, '版本:');
        var plus = v.indexOf('+');
        if (plus > 0) {
            v = v.substring(0, plus);
        }
        if (v.length > 0) {
            return '版本信息 ' + name + ' · ' + v;
        }
        return '版本信息 ' + name + ovStat(r);
    }
});
