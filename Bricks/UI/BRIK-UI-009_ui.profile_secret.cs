// ═══════════════════════════════════════════════════
// 积木: ui.profile_secret
// ID:   BRIK-UI-009
// 类别: UI
// 作用: 设置 LLM 档案密钥——flat 载荷（profileId|key），CredentialStore 隔离不落明文
// 依赖: 无
// 引用: Mau.Runtime
// 原理: flat.Split('|') 两段 → 委托 LlmBridge.SetProfileSecret
// 常用: ConfigTab API Key 输入 → Chat_UI_ProfileSecret → UiPet → 本积木
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.profile_secret 设置 LLM 档案密钥（依赖 LlmBridge）
    /// </summary>
    public static class UiProfileSecretBrick
    {
        /// <summary>
        /// 设置 LLM 档案密钥——flat 载荷 "profileId|key"；CredentialStore 隔离（不落明文）；key 空=清除
        /// </summary>
        /// <param name="flat">两段式载荷（| 分隔）</param>
        /// <returns>true=成功</returns>
        public static bool ProfileSecret(string flat)
        {
            if (flat == null)
            {
                return false;
            }
            string[] parts = flat.Split('|');
            if (parts.Length < 1)
            {
                return false;
            }
            string profileId = parts[0];
            string key = parts.Length > 1 ? parts[1] : "";
            if (profileId.Length == 0)
            {
                return false;
            }
            LlmBridge.SetProfileSecret(profileId, key);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:B7870C12B89E5300E0774283D3AE2D8BB8F326ADE5777AA921082E9C86B74567
