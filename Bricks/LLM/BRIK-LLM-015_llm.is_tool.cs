// ═══════════════════════════════════════════════════
// 积木: llm.is_tool
// ID:   BRIK-LLM-015
// 类别: LLM
// 作用: 判断最后消费分片是否含工具调用——返回 true=含工具
// 依赖: 无
// 引用: 无
// 原理: 读会话 LastChunk → ToolCallsJson 非空判断（返回=判断结果）
// 常用: TalkCat 工具分支（T_CheckTool）
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.is_tool 工具调用判断（依赖 LlmBridge/LlmSession）
    /// </summary>
    public static class LlmIsToolBrick
    {
        /// <summary>
        /// 判断最后消费分片是否含工具调用——返回 true=含工具；false=不含（含查询失败）
        /// </summary>
        /// <param name="requestId">流式会话 ID</param>
        /// <param name="isTool">是否含工具调用</param>
        /// <param name="toolCallsJson">工具调用 JSON（含工具时输出）</param>
        /// <returns>true=最后分片含工具调用</returns>
        public static bool IsTool(string requestId, out bool isTool, out string toolCallsJson)
        {
            isTool = false;
            toolCallsJson = "";
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
            isTool = last.ToolCallsJson.Length > 0;
            toolCallsJson = last.ToolCallsJson;
            return isTool;
        }
    }
}
// #MAU_CHECKSUM:SHA256:58615FE218F5CD09FCCC2E1E0BE3DA55005006F49C40B7C56642E576E51BA541
