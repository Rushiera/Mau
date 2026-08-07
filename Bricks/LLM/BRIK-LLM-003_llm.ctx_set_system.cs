// ═══════════════════════════════════════════════════
// 积木: llm.ctx_set_system
// ID:   BRIK-LLM-003
// 类别: LLM
// 作用: 设置唯一 System Prompt——空文本表示移除
// 依赖: 无
// 引用: 无
// 原理: 会话表写入 SystemPrompt + 历史头部 System 消息同步
// 常用: TalkCat 初始化（T_Init）
// ═══════════════════════════════════════════════════

using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// 上下文积木——llm.ctx_set_system 设置 System Prompt（依赖 ContextStore）
    /// </summary>
    public static class CtxSetSystemBrick
    {
        /// <summary>
        /// 设置唯一 System Prompt——空文本表示移除
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="prompt">System Prompt</param>
        /// <returns>true=成功</returns>
        public static bool CtxSetSystem(string sessionKey, string prompt)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            lock (ContextStore.Gate)
            {
                session.SystemPrompt = ContextStore.SafeText(prompt);
                for (int i = session.History.Count - 1; i >= 0; i = i - 1)
                {
                    if (session.History[i].Role == "System")
                    {
                        session.History.RemoveAt(i);
                    }
                }
                if (session.SystemPrompt.Length > 0)
                {
                    session.History.Insert(0, ContextStore.CreateMessage("System", session.SystemPrompt));
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:9A75A5F9567788461E1AEBA770CDB12B0B4A854BC1C9A8F67BBA1473DCCEDD39
