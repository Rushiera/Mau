// ═══════════════════════════════════════════════════
// 积木: llm.ctx_push_system
// ID:   BRIK-LLM-025
// 类别: LLM
// 作用: 追加 System 角色消息——轮次统计行/系统提示（UI 可见，不入 LLM 请求——会话历史展示用）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: 会话表历史追加 System 角色消息（ContextStore——快照全角色渲染）
// 常用: TalkCat 语料 T_Stats——轮次统计行入上下文（CH2 tokenInfo System 条目移植 2026-08-10）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.ctx_push_system 追加 System 消息（依赖 ContextStore）
    /// </summary>
    public static class CtxPushSystemBrick
    {
        /// <summary>
        /// 追加 System 角色消息——统计行/系统提示（会话历史展示用）
        /// </summary>
        /// <param name="sessionKey">会话 Key</param>
        /// <param name="content">消息正文</param>
        /// <returns>true=成功</returns>
        public static bool CtxPushSystem(string sessionKey, string content)
        {
            ContextSession session = ContextStore.GetOrCreate(ContextStore.SafeKey(sessionKey));
            string safeContent = ContextStore.SafeText(content);
            if (safeContent.Length > 0)
            {
                lock (ContextStore.Gate)
                {
                    LlmMessage message = ContextStore.CreateMessage("System", safeContent);
                    session.History.Add(message);
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:95795463A3EFE00CA68C440D6D756F94A04990E898BB6C7D6BF9016A5BDB9E6E
