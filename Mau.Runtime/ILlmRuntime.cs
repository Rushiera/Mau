using System.Collections.Generic;
using System.Threading;

namespace Mau.Runtime
{
    /// <summary>
    /// LLM 运行时接口——OpenAI 兼容消息序列形态（P5 工具协调升级，旧字符串方法退役）。
    /// 宿主 Bind 注入实现：DataBox.Bind&lt;ILlmRuntime&gt;(new DeepSeekLlmRuntime(...))。
    /// </summary>
    public interface ILlmRuntime
    {
        /// <summary>
        /// 流式对话完成——消息序列 + 工具定义 → 事件流（文本/思考增量 + 工具调用 + 完成）。
        /// </summary>
        /// <param name="messages">完整消息序列（OpenAI 兼容 role：system/user/assistant/tool）</param>
        /// <param name="tools">工具定义数组（可为空——纯对话）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>流式事件序列</returns>
        System.Collections.Generic.IAsyncEnumerable<LlmStreamEvent> ChatStream(LlmMessage[] messages, ToolSpec[] tools, CancellationToken ct = default);
    }
}
