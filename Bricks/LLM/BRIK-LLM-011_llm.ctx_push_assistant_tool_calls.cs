// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_assistant_tool_calls
// ID:   BRIK-LLM-011
// 类别: LLM
// 作用: 追加 Assistant 工具声明消息——LLM 请求了工具（OpenAI 协议 assistant tool_calls）
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 Assistant 角色消息（tool_calls JSON 挂载）
// 常用: TalkCat 工具声明入上下文（T_RecordToolCalls）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_assistant_tool_calls 工具声明（依赖 ContextStore）
    /// </summary>
    public static class CtxPushAssistantToolCallsBrick
    {
        /// <summary>
        /// 追加 Assistant 工具声明消息——LLM 请求了工具（OpenAI 协议 assistant tool_calls）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="toolCallsJson">tool_calls JSON 数组</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushAssistantToolCalls(string sessionKey, string toolCallsJson)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeCalls = ContextStore.SafeText(toolCallsJson);
            if (safeCalls.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    LlmMessage message = ContextStore.CreateMessage("Assistant", "");
                    message.ToolCallsJson = safeCalls;
                    session.History.Add(message);
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:5498B408F91CC8225D1A29DCC54B6C357940BC8350BE6ADEC28319BD082B636C
