// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_user
// ID:   BRIK-LLM-004
// 类别: LLM
// 作用: 追加 User 消息
// 依赖: 无
// 引用: 无
// 原理: 会话表历史追加 User 角色消息（非空正文）
// 常用: TalkCat 用户消息入上下文（T_PushUser）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_push_user 追加 User 消息（依赖 ContextStore）
    /// </summary>
    public static class CtxPushUserBrick
    {
        /// <summary>
        /// 追加 User 消息
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="text">正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushUser(string sessionKey, string text)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeText = ContextStore.SafeText(text);
            if (safeText.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    session.History.Add(ContextStore.CreateMessage("User", safeText));
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:7C716BAA5AE16CB5A94BB1F8CE4F044DB2921740E2FF75799D1FB6E0192E65D0
