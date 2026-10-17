// ═══════════════════════════════════════════
// chat/tools/audio/audio-play.js —— audio-play / 播放本地 WAV（AudioCat）
// 声明：图标 🔈 + 输入意图行（路径 / 分组）+ 折叠行与输出自然语言化；段结构归骨架 `text`。
// 回执形态（audio_cat.mau T_Reply）：成功 `OK|已入队`；失败 `ERR|…`。
// ═══════════════════════════════════════════

toolDecl('audio-play', {
    icon: '🔈',
    inputLines: function (a) {
        var key = (typeof a.key === 'string' && a.key.length > 0) ? ('（分组 ' + a.key + '）') : '';
        return ['播放音频 ' + ovText(a.path) + key];
    },
    headline: function (a, r) {
        var head = '播放音频 ' + ovShort(a.path);
        if (r === undefined) {
            return head + ' · 播放中';
        }
        if (ovOkTail(r).length > 0) {
            return head + ' · 已入队';
        }
        return head + ovStat(r);
    },
    badge: function (r) {
        if (r === undefined) {
            return ' · 播放中';
        }
        return (ovOkTail(r).length > 0) ? ' · 已入队' : '';
    },
    outputLines: function (r, a, isErr) {
        if (isErr) {
            return null;
        }
        if (r === undefined) {
            return ['播放中…'];
        }
        if (ovOkTail(r).length > 0) {
            return ['已入队播放：' + ovText(a.path)];
        }
        return null;
    }
});
