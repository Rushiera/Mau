// ═══════════════════════════════════════════════════
// 积木: llm.ctx_trim
// ID:   BRIK-LLM-006
// 类别: LLM
// 作用: 按字符预算从最早业务消息删除——System 永久保留
// 依赖: 无
// 引用: 无
// 原理: 超预算循环删除最早非 System 消息
// 常用: 长会话预算控制
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_trim 按预算裁剪（依赖 ContextStore）
    /// </summary>
    public static class CtxTrimBrick
    {
        /// <summary>
        /// 按字符预算从最早业务消息删除——System 永久保留
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="maxChars">最大字符预算</param>
        /// <param name="removed">删除的消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxTrim(string sessionKey, int maxChars, out int removed)
        {
            removed = 0;
            if (maxChars < 0)
            {
                return false;
            }
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                while (ContextStore.CountAllChars(session) > maxChars)
                {
                    int start = 0;
                    if (session.History.Count > 0 && session.History[0].Role == "System")
                    {
                        start = 1;
                    }
                    if (start >= session.History.Count)
                    {
                        return true;
                    }
                    session.History.RemoveAt(start);
                    removed = removed + 1;
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:F047D84FE44D3C2E41E6E45C9D92DFCDBACCFBAB3FD10E5DF8236B53F34A1A93
