// ═══════════════════════════════════════════════════
// 积木: llm.has_error
// ID:   BRIK-LLM-016
// 类别: LLM
// 作用: 判断最后消费分片是否带 LLM 错误——错误码是正常返回值（业务层消费）
// 依赖: 无
// 引用: 无
// 原理: 读会话 LastChunk → ErrorCode 非空判断（返回=判断结果，与 is_end/is_tool 同语义）
// 常用: TalkCat 错误消费（T_CheckError——P2.2a）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.has_error 错误码判断（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmHasErrorBrick
    {
        /// <summary>
        /// 判断最后消费分片是否带 LLM 错误——返回=判断结果（true=有错误；false=无错误或不可判）
        /// 语义与 is_end/is_tool 同款：错误码是 LLM 的正常返回值，业务层消费（终止+回滚），非框架级错误
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="hasError">是否带错误码</param>
        /// <param name="errorCode">错误码（有错误时输出）</param>
        /// <returns>true=最后分片带错误码</returns>
        public static bool HasError(string requestId, out bool hasError, out string errorCode)
        {
            hasError = false;
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
            hasError = last.ErrorCode.Length > 0;
            errorCode = last.ErrorCode;
            return hasError;
        }
    }
}
// #MAU_CHECKSUM:SHA256:3686E31F9515CA6460D1F888A2C28598DFF6551A6887BF11ABE7453E8EA690E4
