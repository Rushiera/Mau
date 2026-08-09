// ═══════════════════════════════════════════════════
// 积木: llm.ctx_rollback
// ID:   BRIK-LLM-018
// 类别: LLM
// 作用: 回滚到断点——移除断点之后的所有消息
// 依赖: 无
// 引用: 无
// 原理: RemoveRange 断点之后的全部消息（错误自救——LLM 错误 → 终止 + 回滚）
// 常用: TalkCat 错误回滚（T_Rollback）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_rollback 回滚到断点（依赖 ContextStore）
    /// </summary>
    public static class CtxRollbackBrick
    {
        /// <summary>
        /// 回滚到断点——移除断点之后的所有消息（System 之前的永久消息保留）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="checkpoint">断点（消息数量）</param>
        /// <returns>true=成功</returns>
        public static bool CtxRollback(string sessionKey, int checkpoint)
        {
            if (checkpoint <= 0)
            {
                return false;
            }
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                if (checkpoint >= session.History.Count)
                {
                    return true;
                }
                session.History.RemoveRange(checkpoint, session.History.Count - checkpoint);
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:A28A969F77A797AD8C72F50A6C0B29A473DFD6717BF70FF03D48AC69DE0F414C
