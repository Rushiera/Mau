// ═══════════════════════════════════════════════════
// 积木: llm.done_ready
// ID:   BRIK-LLM-004
// 类别: LLM
// 作用: 完成探测——llm_done == "1" → true（判断语义：值比较归宿主）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: 读全局盒 llm_done（"1"=流式完成/失败已结算）
// 盒子: 读全局 llm_done（B1 豁免）
// 常用: CH4 P4 quick_cat 语料——完成信号（主动传感器壳探测；Done 状态转移后条件自然失效）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.done_ready 完成探测（判断语义：llm_done 置位才 true）
    /// </summary>
    public static class LlmDoneReadyBrick
    {
        /// <summary>
        /// 完成探测——llm_done 全局盒已置 "1"
        /// </summary>
        /// <returns>true=流式已完成（llm_reply 可读）</returns>
        public static bool DoneReady()
        {
            string done;
            if (!DataBox.TryGet<string>("global", "llm_done", out done))
            {
                return false;
            }
            return done == "1";
        }
    }
}
// #MAU_CHECKSUM:SHA256:3FC4C632A5CBB0017D82054A61DCE30DACFB538845D4A00D8EE7FABB03AD854A
