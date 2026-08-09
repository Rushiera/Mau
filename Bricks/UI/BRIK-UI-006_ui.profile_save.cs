// ═══════════════════════════════════════════════════
// 积木: ui.profile_save
// ID:   BRIK-UI-006
// 类别: UI
// 作用: LLM 档案保存——JSON 单条（ProfileId 空=新建），密钥不入 JSON
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 委托 LlmBridge.SaveProfile——JSON 反序列化 → 保存 → 返回最终档案 Id
// 常用: ConfigTab 保存档案按钮 → Chat_UI_ProfileSave → UiPet → 本积木
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.profile_save LLM 档案保存（依赖 LlmBridge）
    /// </summary>
    public static class UiProfileSaveBrick
    {
        /// <summary>
        /// 保存 LLM 档案——JSON 单条（无密钥）；ProfileId 空=新建
        /// </summary>
        /// <param name="json">档案 JSON</param>
        /// <param name="profileId">最终档案 Id（新建时宿主生成）</param>
        /// <returns>true=成功</returns>
        public static bool ProfileSave(string json, out string profileId)
        {
            profileId = "";
            if (json == null || json.Length == 0)
            {
                return false;
            }
            try
            {
                System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions();
                options.IncludeFields = true;
                LlmProfile profile = System.Text.Json.JsonSerializer.Deserialize<LlmProfile>(json, options);
                if (profile == null)
                {
                    return false;
                }
                profileId = LlmBridge.SaveProfile(profile);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
// #MAU_CHECKSUM:SHA256:0B56AA816750CD1349812F38152C7D398D3AEF8534426431704E753BF968EBCA
