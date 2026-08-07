// ═══════════════════════════════════════════════════
// 积木: llm.ctx_clear
// ID:   BRIK-LLM-009
// 类别: LLM
// 作用: 清除业务历史并恢复唯一 System Prompt
// 依赖: 无
// 引用: 无
// 原理: 历史清空 → 保留 SystemPrompt 重插 System 消息
// 常用: 会话开始重置 / 测试隔离
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_clear 清除历史（依赖 ContextStore）
    /// </summary>
    public static class CtxClearBrick
    {
        /// <summary>
        /// 清除业务历史并恢复唯一 System Prompt
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <returns>true=成功</returns>
        public static bool CtxClear(string sessionKey)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                session.History.Clear();
                if (session.SystemPrompt.Length > 0)
                {
                    session.History.Add(ContextStore.CreateMessage("System", session.SystemPrompt));
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:10B9E154957C48E4CECD7B0B40F1D5EA5C1791AFD150D03FE0F10DBA0D4B17D5
