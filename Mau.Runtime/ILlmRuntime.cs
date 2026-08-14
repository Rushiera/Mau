namespace Mau.Runtime
{
    /// <summary>
    /// LLM 运行时接口——非流式一次完成（第一轮最小面）。
    /// 流式/工具调用/reasoning 随 OS 能力阶段扩展（Project/Mau/design-llm-streaming.md 重生规格）。
    /// 宿主 Bind 注入实现：DataBox.Bind&lt;ILlmRuntime&gt;(new DeepSeekLlmRuntime(...))。
    /// </summary>
    public interface ILlmRuntime
    {
        /// <summary>
        /// 非流式完成——system + content → reply。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="reply">回复——失败时携带 ERR| 错误文本</param>
        /// <returns>true=成功</returns>
        bool Completions(string system, string content, out string reply);
    }
}
