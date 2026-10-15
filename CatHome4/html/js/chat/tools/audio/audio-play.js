// ═══════════════════════════════════════════
// chat/tools/audio/audio-play.js —— audio-play / 播放本地 WAV（AudioCat）
// 声明：图标 🔈 + 输入意图行；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('audio-play', {
    icon: '🔈',
    inputLines: function (a) {
        var key = ovText(a.key);
        var tail = (key.length > 0) ? ('（分组 ' + key + '）') : '';
        return ['播放音频 ' + ovText(a.path) + tail];
    }
});
