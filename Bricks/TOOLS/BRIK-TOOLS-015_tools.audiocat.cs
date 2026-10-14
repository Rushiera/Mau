// ═══════════════════════════════════════════════════
// 积木: tools.audiocat
// ID:   BRIK-TOOLS-015
// 类别: TOOLS
// 作用: 返回 AudioCat 工具组全部工具（audio-play 1 件）的 OpenAI 兼容定义 JSON（宿主工具池拼装原料）
// 依赖: 无
// 引用: 无（纯文本常量）
// 原理: GetToolsJson() 返回本组工具定义整包 JSON——改描述只动本文件，重建即生效
// 常用: FL_AudioCat.GetToolsJson() 自曝调用面（IFlow 接口）
// ═══════════════════════════════════════════════════
namespace Mau.Bricks
{
    /// <summary>
    /// 工具定义积木——AudioCat 组工具定义 JSON（OpenAI 兼容拼装原料）
    /// </summary>
    public static class ToolsAudiocatBrick
    {
        /// <summary>
        /// 获取本组工具定义 JSON——{"group":"AudioCat","tools":[{name,description,parameters}]}
        /// </summary>
        /// <returns>工具定义 JSON 字符串</returns>
        public static string GetToolsJson()
        {
            return "{\"group\":\"AudioCat\",\"tools\":[" +
                "{\"name\":\"audio-play\",\"description\":\"播放本地 WAV 音频文件——同 key 的音频按先后串行播放、不同 key 并发；path 必填，key 用于串行分组（省略则所有音频默认串行），volume 0~1 可选（省略=用全局配置音量）\",\"parameters\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"WAV 文件路径\"},\"key\":{\"type\":\"string\",\"description\":\"串行分组键（同 key 排队；省略 = 默认串行组）\"},\"volume\":{\"type\":\"number\",\"description\":\"会话音量 0~1（省略=用全局配置 audio.volume）\"}},\"required\":[\"path\"]}}" +
                "]}";
        }
    }
}
// #MAU_CHECKSUM:SHA256:77A01C4703601DA0C6205DB1B4D3D58DD36BC6D3F3C79B5032BD911997F78DC1
