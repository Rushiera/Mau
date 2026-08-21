// ═══════════════════════════════════════════════════
// 积木: llm.done_ready
// ID:   BRIK-LLM-004
// 类别: LLM
// 作用: 完成探测——本实例 llm_done:<f> == "1" → true（判断语义：值比较归宿主）
// 依赖: 无
// 引用: Mau.Runtime（DataBox/FlowContext）
// 原理: 读本实例完成段盒 llm_done:<f>（"1"=流式完成/失败已结算）
// 盒子: 读全局 llm_done:<f>（B1 豁免）
// 常用: CH4 P4 quick_cat 语料——完成信号（主动传感器壳探测；Done 状态转移后条件自然失效）
// 注意: P9.2 Key 段化——按 FlowContext.CurrentFlowId 取实例段（多实例隔离）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.done_ready 完成探测（判断语义：本实例 llm_done 置位才 true）
    /// </summary>
    public static class LlmDoneReadyBrick
    {
        /// <summary>
        /// 完成探测——本实例 llm_done:<f> 段盒已置 "1"（flowId 无上下文时 false——语料外调用防御）
        /// </summary>
        /// <returns>true=流式已完成（llm_reply 可读）</returns>
        public static bool DoneReady()
        {
            long flowId = FlowContext.CurrentFlowId;
            if (flowId <= 0)
            {
                return false;
            }
            string done;
            if (!DataBox.TryGet<string>("global", "llm_done:" + flowId.ToString(), out done))
            {
                return false;
            }
            return done == "1";
        }
    }
}
// #MAU_CHECKSUM:SHA256:9CDE500FF019FFB7FC78454B5D7613ECEE614EC4C48E868ED75CF68770664C59
