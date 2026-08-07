// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_assistant
// ID:   BRIK-LLM-005
// 类别: LLM
// 作用: 追加 Assistant 消息
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 Assistant 角色消息（非空正文）
// 常用: TalkCat 流式内容回填（T_Append）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_assistant 追加 Assistant 消息（依赖 ContextStore）
    /// </summary>
    public static class CtxPushAssistantBrick
    {
        /// <summary>
        /// 追加 Assistant 消息
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushAssistant(string sessionKey, string text)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeText = ContextStore.SafeText(text);
            if (safeText.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    session.History.Add(ContextStore.CreateMessage("Assistant", safeText));
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:657941A1BB0C963B38AA8B28B678FE80923804D2A8C41F3BA5F8A78CE05D0E89
