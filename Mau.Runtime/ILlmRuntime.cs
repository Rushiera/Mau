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

        /// <summary>
        /// 流式完成——思考/回复增量事件流（P4 最小面）。
        /// Text/Reasoning 增量；Done 正常结束（[DONE] 到达）；Error 失败终止（Text 携带 ERR|码|详情）。
        /// </summary>
        /// <param name="system">系统提示词</param>
        /// <param name="content">用户内容</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>流式事件序列</returns>
        System.Collections.Generic.IAsyncEnumerable<LlmStreamEvent> StreamCompletions(string system, string content, System.Threading.CancellationToken ct = default);
    }
}
