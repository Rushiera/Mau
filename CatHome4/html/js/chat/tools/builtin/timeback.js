// ═══════════════════════════════════════════
// chat/tools/builtin/timeback.js —— timeback / 上下文作用域（内置）
// 声明：专属图标 ⚓ + 折叠行（锚号 / 起点 / 改动处数）+ 输入意图行（开锚 / 回收两态）；
//       输出段台账可视化——宿主台账段加 ⚙ 前缀并与自述面分离（2026-10-02 口径）。
// ═══════════════════════════════════════════

toolDecl('timeback', {
    icon: '⚓',
    inputLines: function (a) {
        if (a.action === 'back') {
            return ['TimeBack 回收', '载荷 ' + ovPeek(a.findings)];
        }
        return ['TimeBack 开锚' + ((a.purpose) ? (' · ' + ovPeek(a.purpose)) : '')];
    },
    headline: function (a, r) {
        var h = ovHead(r);
        if (!h) {
            return ((a.action === 'back') ? 'TimeBack 回收' : 'TimeBack 开锚') + ovStat(r);
        }
        var m = h.meta;
        var writes = ovMetaNum(m, 'writes', -1);
        var w = (writes > 0) ? (' · 改动 ' + writes + ' 处') : '';
        var purpose = ovMetaStr(m, 'purpose');
        var purposeTail = (purpose.length > 0) ? (' · ' + ovPeek(purpose)) : '';
        if (a.action === 'back') {
            return 'TimeBack #' + ovMetaNum(m, 'id', 0) + ' 已登记回收 · 锚点 ' + ovMetaStr(m, 'anchor') + w + purposeTail;
        }
        return 'TimeBack #' + ovMetaNum(m, 'id', 0) + ' 已锚定 · 起点 ' + ovMetaStr(m, 'anchor') + purposeTail;
    },
    outputLines: function (r) {
        var h = ovHead(r);
        if (!h) {
            return null;
        }
        var body = h.body;
        if (typeof body !== 'string' || body.length === 0) {
            return null;
        }
        // 无台账段回落骨架默认渲染（覆盖契约：只声明偏离骨架的项）
        if (body.indexOf('[本域写操作台账') < 0) {
            return null;
        }
        var lines = body.replace(/\r/g, '').split('\n');
        var out = [];
        var inWrites = false;
        for (var i = 0; i < lines.length; i = i + 1) {
            var t = lines[i];
            if (t.indexOf('[本域写操作台账') === 0) {
                inWrites = true;
                out.push('⚙ ' + t);
                continue;
            }
            if (inWrites) {
                if (t.length === 0) {
                    inWrites = false;
                    continue;
                }
                out.push('   ' + t);
                continue;
            }
            out.push(t);
        }
        return out;
    }
});
