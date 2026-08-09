// ═══════════════════════════════════════════════════
// 积木: ui.profile_active
// ID:   BRIK-UI-008
// 类别: UI
// 作用: 切换生效 LLM 档案（设为默认）
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 委托 LlmBridge.SetActiveProfile
// 常用: ConfigTab 设为默认按钮 → Chat_UI_ProfileActive → UiPet → 本积木
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.profile_active 切换生效 LLM 档案（依赖 LlmBridge）
    /// </summary>
    public static class UiProfileActiveBrick
    {
        /// <summary>
        /// 切换生效 LLM 档案
        /// </summary>
        /// <param name="profileId">档案 Id</param>
        /// <returns>true=成功</returns>
        public static bool ProfileActive(string profileId)
        {
            if (profileId == null || profileId.Length == 0)
            {
                return false;
            }
            LlmBridge.SetActiveProfile(profileId);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A18C4C041B8F271AD282BF3E845D93AC0A428257A5042697B9E2651892F32D1A
