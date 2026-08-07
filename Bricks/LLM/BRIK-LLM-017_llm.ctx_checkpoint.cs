// ═══════════════════════════════════════════════════
// 积木: llm.ctx_checkpoint
// ID:   BRIK-LLM-017
// 类别: LLM
// 作用: 记录回滚断点——当前消息数量（对话开始前调用；错误时回滚到此处）
// 依赖: 无
// 引用: 无
// 原理: 锁内读历史 Count 作为断点
// 常用: TalkCat 错误消费（T_Checkpoint）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_checkpoint 记录回滚断点（依赖 ContextStore）
    /// </summary>
    public static class CtxCheckpointBrick
    {
        /// <summary>
        /// 记录回滚断点——当前消息数量（对话开始前调用；错误时回滚到此处）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="checkpoint">断点（消息数量）</param>
        /// <returns>true=成功</returns>
        public static bool CtxCheckpoint(string sessionKey, out int checkpoint)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                checkpoint = session.History.Count;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:36D3616FAAA05B56B39F8D04AC89C1A5A1BD30EB311C04ECC2D0B8DBD373F507
