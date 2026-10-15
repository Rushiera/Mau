// ═══════════════════════════════════════════
// chat/tools/audio/audio-speak.js —— audio-speak / 文本转语音并播放（AudioCat · A246）
// 声明：图标 🗣 + 输入意图行（文本 / 音色）；段结构归骨架 `text`。
// ═══════════════════════════════════════════

toolDecl('audio-speak', {
    icon: '🗣',
    inputLines: function (a) {
        var voice = ovText(a.voice);
        var tail = (voice.length > 0) ? ('（音色 ' + voice + '）') : '';
        return ['合成并播放「' + ovText(a.text) + '」' + tail];
    }
});
