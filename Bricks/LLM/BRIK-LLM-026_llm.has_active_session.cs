// ═══════════════════════════════════════════════════
// 积木: llm.has_active_session
// ID:   BRIK-LLM-026
// 类别: LLM
// 作用: 是否存在活跃 LLM 会话——流式中判定源（P2-6：busy 轮询的门控）
// 依赖: 无
// 包: 无
// 引用: Mau.Runtime
// 原理: LlmSession.HasActiveSession（llm.finish 移除会话后 false）
// 常用: TalkCat 语料 T_CheckBusyGo——流式中 Stop 轮询门控（空闲时跳过 consume，防抢占空闲指令）
// ═══════════════════════════════════════════════════
using Mau.Runtime;

namespace Mau.Bricks
{
    /// <summary>
    /// LLM 积木——llm.has_active_session 活跃会话判定（依赖 LlmSession）
    /// </summary>
    public static class LlmHasActiveSessionBrick
    {
        /// <summary>
        /// 是否存在活跃 LLM 会话——true=流式中（busy 轮询门控）；false=空闲（跳过 consume）
        /// </summary>
        /// <returns>true=有活跃会话</returns>
        public static bool HasActiveSession()
        {
            return LlmSession.HasActiveSession();
        }
    }
}
// #MAU_CHECKSUM:SHA256:72D69C62BAB845DB61E5AB8E36033898D6D80F812A4EF530938E797AFAD9A810
