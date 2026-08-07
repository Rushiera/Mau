// ═══════════════════════════════════════════════════
// 积木: llm.is_end
// ID:   BRIK-LLM-014
// 类别: LLM
// 作用: 判断最后消费分片是否终态——返回 true=终态（流结束）
// 依赖: 无
// 引用: 无
// 原理: 读会话 LastChunk → Finished/ErrorCode 输出（返回=判断结果）
// 常用: TalkCat 流式终态分类（T_Classify）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.is_end 终态判断（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmIsEndBrick
    {
        /// <summary>
        /// 判断最后消费分片是否终态——返回 true=终态（流结束）；false=非终态（含查询失败）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="ended">终态标志</param>
        /// <param name="errorCode">终态且失败时非空</param>
        /// <returns>true=最后分片是终态</returns>
        public static bool IsEnd(string requestId, out bool ended, out string errorCode)
        {
            ended = false;
            errorCode = "";
            LlmStreamSession? session = LlmSession.FindSession(requestId);
            if (session == null)
            {
                return false;
            }
            LlmStreamChunk? last = session.LastChunk;
            if (last == null)
            {
                return false;
            }
            ended = last.Finished;
            errorCode = last.ErrorCode;
            return ended;
        }
    }
}
// #MAU_CHECKSUM:SHA256:F664ADEECDA17E9E61671FFA3ECD307359D41D481289A4D9665FCD266DFDFF45
