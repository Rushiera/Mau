// ═══════════════════════════════════════════════════
// 积木: ui.config_get
// ID:   BRIK-UI-011
// 类别: UI
// 作用: 读配置快照——按猫 ConfigStore 五个会话键合成 JSON
// 依赖: 无
// 引用: Mau.Runtime · System.Text
// 原理: DataBox.TryGet<ConfigStore>("catcfg", catName) → Get 五键（fontSize/thinkMode/cmdConfirm/theme/whitelist）；
//       PascalCase——与 C# 字段默认匹配
// 常用: HomeTab 选中猫 → Chat_UI_Select → UiPet → 本积木 → push "ui.catcfg"
// 包: 无
// ═══════════════════════════════════════════════════
using System.IO;
using System.Text;

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.config_get 读配置快照（依赖宿主 Bind 的 ConfigStore 服务）
    /// </summary>
    public static class UiConfigGetBrick
    {
        /// <summary>
        /// 读配置快照——五个会话键合成 JSON（FontSize/ThinkMode/CmdConfirm/Theme/Whitelist）
        /// </summary>
        /// <param name="catName">猫名</param>
        /// <param name="json">配置 JSON</param>
        /// <returns>true=成功</returns>
        public static bool ConfigGet(string catName, out string json)
        {
            json = "";
            if (catName == null || catName.Length == 0)
            {
                return false;
            }
            ConfigStore? store;
            DataBox.TryGet<ConfigStore>("catcfg", catName, out store);
            if (store == null)
            {
                return false;
            }
            using (MemoryStream stream = new MemoryStream())
            {
                using (System.Text.Json.Utf8JsonWriter writer = new System.Text.Json.Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("FontSize", store.Get("fontSize", "medium"));
                    writer.WriteString("ThinkMode", store.Get("thinkMode", "partial"));
                    writer.WriteString("CmdConfirm", store.Get("cmdConfirm", "ask"));
                    writer.WriteString("Theme", store.Get("theme", "灰色"));
                    writer.WriteString("Whitelist", store.Get("whitelist", ""));
                    writer.WriteString("Tools", store.Get("tools", ""));
                    writer.WriteString("LlmProfile", store.Get("llmProfile", ""));
                    writer.WriteEndObject();
                }
                json = Encoding.UTF8.GetString(stream.ToArray());
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:F520A1D494484B889599879AC3A2CF34664A0964AFE6A62CFB369A714C356D7C
