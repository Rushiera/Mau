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
    }
}
