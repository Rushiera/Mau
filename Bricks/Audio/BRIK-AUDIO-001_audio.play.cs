// ═══════════════════════════════════════════════════
// 积木: audio.play
// ID:   BRIK-AUDIO-001
// 类别: AUDIO
// 作用: 播放本地 WAV 音频（per-Key 串行 + 组间并发，投递即返回）——LLM 工具 audio-play 的语料执行面
// 依赖: 无
// 引用: Mau.Runtime（DataBox/JsonArgs/WorkspaceConfig）· Mau.Contracts（IAudioService）
// 原理: JsonArgs 解析 key/path/volume → path 根寻址展开 → DataBox.TryResolve<IAudioService> → Enqueue（入队即返回；调度全在服务内）
// 常用: audio_cat.mau 认领线——'audio.play'[@adArgs] > @adRes
// 注意: 只进不出——入队成功不等待播放完成（完成信号不回业务；design-mau-audio §四）
// ═══════════════════════════════════════════════════
using System;
using System.Globalization;
using Mau.Contracts;
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 音频积木——audio.play 播放 WAV（LLM 工具执行面：参数整包 argsJson）。
    /// </summary>
    public static class AudioPlayBrick
    {
        /// <summary>
        /// 入队播放——起播或按 Key 排队，立即返回。
        /// </summary>
        /// <param name="argsJson">工具参数 JSON（path 必填；key / volume 可选）</param>
        /// <param name="result">成功 "OK|已入队"；失败 ERR| 文本</param>
        /// <returns>true=已受理（起播或入队）</returns>
        public static bool Play(string argsJson, out string result)
        {
            result = "";
            // [参数面] 声明面口径零容忍——未知 / 缺值一律 ERR|BAD_ARGS（catId 保留键放行）
            string badArgs = JsonArgs.Validate(argsJson, "key path volume", "path", "", "");
            if (badArgs.Length > 0)
            {
                result = badArgs;
                return false;
            }
            string path = JsonArgs.Get(argsJson, "path");
            if (path.Length == 0)
            {
                result = "ERR|BAD_ARGS|缺少参数 path";
                return false;
            }
            // [段1] 根寻址展开——顶层设计：<根id>:<相对> 映射受控根（与 py-run script 同口径）
            path = ExpandPath(path);
            string key = JsonArgs.Get(argsJson, "key");
            if (key == "§PARSE_FAIL§")
            {
                key = "";
            }
            // 音量——未指定（volume=-1）交由服务读全局配置 audio.volume（design-mau-audio §五）
            float volume = -1f;
            string volText = JsonArgs.Get(argsJson, "volume");
            if (volText.Length > 0 && volText != "§PARSE_FAIL§")
            {
                float parsed;
                if (float.TryParse(volText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    volume = parsed;
                }
            }
            IAudioService service = null;
            if (!DataBox.TryResolve<IAudioService>(out service) || service == null)
            {
                result = "ERR|AUDIO_NO_SERVICE|宿主未注入 IAudioService";
                return false;
            }
            string reply = service.Enqueue(key, path, volume);
            if (reply != null && reply.Length > 0)
            {
                result = reply;
                return false;
            }
            result = "OK|已入队";
            return true;
        }

        /// <summary>
        /// 根寻址展开——经工作区根表（WorkspaceConfig 全局绑定）；未绑定 / 未知根 id 原样返回。
        /// </summary>
        /// <param name="path">输入路径（根寻址形态或普通路径）</param>
        /// <returns>展开后的路径</returns>
        private static string ExpandPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            WorkspaceConfig workspace = null;
            if (DataBox.TryResolve<WorkspaceConfig>(out workspace) && workspace != null)
            {
                return workspace.ExpandRootPath(path);
            }
            return path;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5D979313A9EE805C47974070FE7301F7F47B242B624E02532F428401A60885F1
