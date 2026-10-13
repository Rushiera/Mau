// ═══════════════════════════════════════════════════
// 积木: llm.simple_ready
// ID:   BRIK-LLM-008
// 类别: LLM
// 作用: 一次性请求完成探测——llm_simple_done:<requestId> == "1" → true（判断语义：值比较归积木，分支归语料）
// 依赖: 无
// 引用: Mau.Runtime（DataBox）
// 原理: 读段盒 llm_simple_done:<requestId>（"1"=后台 HTTP 已结算——成功/失败皆置位）
// 盒子: 读全局 llm_simple_done:<requestId>（B1 豁免）
// 常用: LLMSimpleRequest 服务语料 P_Ready 传感器——完成信号（Done 状态转移后条件自然失效）
// 注意: requestId 取 OA officeId（全局唯一）；键缺失 = 未完成 = false
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.simple_ready 一次性请求完成探测（判断语义：段盒置位才 true）。
    /// </summary>
    public static class LlmSimpleReadyBrick
    {
        /// <summary>
        /// 完成探测——段盒 llm_simple_done:<requestId> 已置 "1"
        /// </summary>
        /// <param name="requestId">请求标识（取 OA officeId）</param>
        /// <returns>true=后台请求已结算（llm_simple_reply 可读）</returns>
        public static bool SimpleReady(long requestId)
        {
            if (requestId <= 0)
            {
                return false;
            }
            string done;
            if (!DataBox.TryGet<string>("global", "llm_simple_done:" + requestId.ToString(), out done))
            {
                return false;
            }
            return done == "1";
        }
    }
}
// #MAU_CHECKSUM:SHA256:076D6A44D68AB215FA66156AAF1D906734DE862D93F32EA8EE70D9BCD020884D
