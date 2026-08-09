// ═══════════════════════════════════════════════════
// 积木: ui.profile_delete
// ID:   BRIK-UI-007
// 类别: UI
// 作用: LLM 档案删除——同时清除其密钥
// 依赖: 无
// 引用: Mau.Runtime
// 原理: 委托 LlmBridge.DeleteProfile
// 常用: ConfigTab 删除档案按钮 → Chat_UI_ProfileDelete → UiPet → 本积木
// 包: 无
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// UI 积木——ui.profile_delete LLM 档案删除（依赖 LlmBridge）
    /// </summary>
    public static class UiProfileDeleteBrick
    {
        /// <summary>
        /// 删除 LLM 档案——同时清除其密钥
        /// </summary>
        /// <param name="profileId">档案 Id</param>
        /// <returns>true=成功</returns>
        public static bool ProfileDelete(string profileId)
        {
            if (profileId == null || profileId.Length == 0)
            {
                return false;
            }
            LlmBridge.DeleteProfile(profileId);
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:8B8B335FDEAA52EC662148767E22C132AEEC2647B67E07C0594E3D227E5E15E9
