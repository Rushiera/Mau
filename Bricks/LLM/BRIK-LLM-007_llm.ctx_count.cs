// ═══════════════════════════════════════════════════
// 积木: llm.ctx_count
// ID:   BRIK-LLM-007
// 类别: LLM
// 作用: 读取当前消息数量
// 依赖: 无
// 引用: 无
// 原理: 会话表历史 Count（锁内）
// 常用: 上下文规模判断 / 轮末复位（T_Reset）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_count 读取消息数量（依赖 ContextStore）
    /// </summary>
    public static class CtxCountBrick
    {
        /// <summary>
        /// 读取当前消息数量
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="count">消息数量</param>
        /// <returns>true=成功</returns>
        public static bool CtxCount(string sessionKey, out int count)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                count = session.History.Count;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:9ABE80A6B3A090FD537D9BEC972299E0C408FE8157076191E96BC0EE9973E250
