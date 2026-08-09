// ═══════════════════════════════════════════════════
// 积木: ui.snapshot_config
// ID:   BRIK-UI-005
// 类别: UI
// 作用: 配置快照合成——LLM 档案 + 全局窗口默认 → ConfigSnapshot JSON
// 依赖: 无
// 引用: Mau.Runtime · System.Text
// 原理: LlmBridge 档案遍历（无密钥——只带 HasSecret 标记）+ 全局 ConfigStore（DataBox "catcfg"/"__global__"）；
//       PascalCase——与 C# 字段默认匹配
// 常用: UiPet 每帧配置快照合成（Pet-UI 模式——ConfigTab/CreateCatTab 只读渲染）
// 包: 无
// ═══════════════════════════════════════════════════
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.snapshot_config 配置快照合成（依赖 LlmBridge + 全局 ConfigStore）
    /// </summary>
    public static class UiSnapshotConfigBrick
    {
        /// <summary>
        /// 合成 ConfigSnapshot JSON——Profiles 数组 + ActiveProfileId + Win 窗口默认
        /// </summary>
        /// <param name="configJson">ConfigSnapshot JSON</param>
        /// <returns>true=成功</returns>
        public static bool SnapshotConfig(out string configJson)
        {
            configJson = "";
            LlmProfile[] profiles = LlmBridge.GetAllProfiles();
            ConfigStore? global;
            DataBox.TryGet<ConfigStore>("catcfg", "__global__", out global);
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("Profiles");
                    writer.WriteStartArray();
                    for (int i = 0; i < profiles.Length; i = i + 1)
                    {
                        LlmProfile profile = profiles[i];
                        writer.WriteStartObject();
                        writer.WriteString("ProfileId", profile.ProfileId);
                        writer.WriteString("DisplayName", profile.DisplayName);
                        writer.WriteString("ApiType", profile.ApiType);
                        writer.WriteString("Endpoint", profile.Endpoint);
                        writer.WriteString("Model", profile.Model);
                        writer.WriteBoolean("HasSecret", LlmBridge.GetProfileSecret(profile.ProfileId).Length > 0);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteString("ActiveProfileId", LlmBridge.ActiveProfileId);
                    // [段1] 窗口级默认——全局 ConfigStore（宿主 Bind "catcfg"/"__global__"）
                    writer.WritePropertyName("Win");
                    writer.WriteStartObject();
                    writer.WriteString("WinX", global != null ? global.Get("win_x", "") : "");
                    writer.WriteString("WinY", global != null ? global.Get("win_y", "") : "");
                    writer.WriteString("WinW", global != null ? global.Get("win_w", "") : "");
                    writer.WriteString("WinH", global != null ? global.Get("win_h", "") : "");
                    writer.WriteString("Font", global != null ? global.Get("font", "中") : "中");
                    writer.WriteString("Theme", global != null ? global.Get("theme", "灰色") : "灰色");
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                configJson = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B6130CC1AF97F2DBC1A17692327CEF2D7EE8C6BAFCEDC56806C3C1FE72758DCA
