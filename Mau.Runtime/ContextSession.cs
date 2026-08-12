using System.Collections.Generic;
using Mau.Runtime;

namespace Mau.Runtime
{
    /// <summary>
    /// 上下文会话——单个 sessionKey 的对话历史 + System Prompt（契约类型——llm.ctx_* 共享）
    /// </summary>
    public sealed class ContextSession
    {
        /// <summary>
        /// 消息列表——按时间顺序
        /// </summary>
        public readonly List<LlmMessage> History = new List<LlmMessage>();

        /// <summary>
        /// 唯一 System Prompt——Clear 时恢复
        /// </summary>
        public string SystemPrompt = "";

        /// <summary>
        /// 累计 prompt token——标题 Token 统计源（G.4 2026-08-11 D.3；llm.ctx_push_stream 完成时累计）
        /// </summary>
        public long TotalPromptTokens;

        /// <summary>
        /// 累计 completion token——标题/统计源（G.4 2026-08-11 D.3）
        /// </summary>
        public long TotalCompletionTokens;

        /// <summary>
        /// 累计缓存命中 token（G.4 2026-08-11 D.3）
        /// </summary>
        public long TotalCacheHitTokens;

        /// <summary>
        /// 已累计轮次数（G.4 2026-08-11 D.3）
        /// </summary>
        public long RoundCount;
    }
}
