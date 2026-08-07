// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_tool
// ID:   BRIK-LLM-010
// 类别: LLM
// 作用: 追加 Tool 结果消息——工具调用回执（OpenAI 协议 role=tool）
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 Tool 角色消息（tool_call_id 绑定）
// 常用: TalkCat 工具结果回填（T_AppendTool）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_tool 追加 Tool 结果（依赖 ContextStore）
    /// </summary>
    public static class CtxPushToolBrick
    {
        /// <summary>
        /// 追加 Tool 结果消息——工具调用回执（OpenAI 协议 role=tool）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="toolCallId">工具调用 ID（assistant tool_calls 对应）</param>
        /// <param name="content">工具结果正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushTool(string sessionKey, string toolCallId, string content)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeContent = ContextStore.SafeText(content);
            string safeId = ContextStore.SafeText(toolCallId);
            if (safeContent.Length > 0 || safeId.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    LlmMessage message = ContextStore.CreateMessage("Tool", safeContent);
                    message.ToolCallId = safeId;
                    session.History.Add(message);
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:60BFAFFF93546887EE29E9102361C9B27541C569A18404D49E5E0C69A3CE2D86
