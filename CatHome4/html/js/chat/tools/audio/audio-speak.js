// ═══════════════════════════════════════════
// chat/tools/audio/audio-speak.js —— audio-speak / 文本转语音并播放（AudioCat · A246）
// 声明：图标 🗣 + 输入意图行（文本 / 音色）+ 折叠行与输出自然语言化；段结构归骨架 `text`。
// 回执形态（audio_cat.mau T_SpeakReply）：成功 `OK|<产物路径>`；失败 `ERR|…`。
// ═══════════════════════════════════════════

toolDecl('audio-speak', {
    icon: '🗣',
    inputLines: function (a) {
        var voice = (typeof a.voice === 'string' && a.voice.length > 0) ? ('（音色 ' + a.voice + '）') : '';
        return ['合成并播放「' + ovPeek(a.text) + '」' + voice];
    },
    headline: function (a, r) {
        var head = '合成语音「' + ovPeek(a.text) + '」';
        if (r === undefined) {
            return head + ' · 合成中';
        }
        if (ovOkTail(r).length > 0) {
            return head + ' · 已播放';
        }
        return head + ovStat(r);
    },
    badge: function (r) {
        if (r === undefined) {
            return ' · 合成中';
        }
        return (ovOkTail(r).length > 0) ? ' · 已播放' : '';
    },
    outputLines: function (r, a, isErr) {
        if (isErr) {
            return null;
        }
        if (r === undefined) {
            return ['合成中…'];
        }
        var out = ovOkTail(r);
        if (out.length > 0) {
            return ['已合成并播放：' + out];
        }
        return null;
    }
});
