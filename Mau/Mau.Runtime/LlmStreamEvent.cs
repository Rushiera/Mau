namespace Mau.Runtime
{
    /// <summary>
    /// 流式事件种类——P5 工具协调扩展：思考/回复双通道 + 工具调用 + 结束。
    /// Text/Reasoning = 增量事件；ToolCalls = 完整工具调用列表（finish=tool_calls 时一次性发出）；Done = 流正常结束；Error = 失败终止。
    /// </summary>
    public enum LlmStreamKind
    {
        /// <summary>
        /// 回复增量（choices[].delta.content）
        /// </summary>
        Text,

        /// <summary>
        /// 思考增量（choices[].delta.reasoning_content）——双通道独立回收
        /// </summary>
        Reasoning,

        /// <summary>
        /// 工具调用到达——Text 携带完整 tool_calls JSON 数组（[{"id","name","arguments"}]）；流随后 Done
        /// </summary>
        ToolCalls,

        /// <summary>
        /// 流正常结束（[DONE] 到达）——Text 为空
        /// </summary>
        Done,

        /// <summary>
        /// 失败终止——Text 携带 ERR|码|详情（错误可见性：禁止吞错）
        /// </summary>
        Error,

        /// <summary>
        /// usage 统计块到达——Text 携带 usage JSON（prompt/completion/cacheHit——E3 Token 统计）
        /// </summary>
        Usage,

        /// <summary>
        /// 重试通知——重试前发出（业务事件未产出，可安全重发）；Text 携带 RETRY|N/3|原因摘要（S2 §8.4——前端独立视图条目）
        /// </summary>
        Retrying
    }

    /// <summary>
    /// 流式事件——增量/工具调用/结束/错误统一载体（不可变）。
    /// </summary>
    public sealed class LlmStreamEvent
    {
        /// <summary>
        /// 事件种类
        /// </summary>
        public LlmStreamKind Kind
        {
            get;
        }

        /// <summary>
        /// 增量文本 / 工具调用 JSON / 错误文本（Done 时为空）
        /// </summary>
        public string Text
        {
            get;
        }

        /// <summary>
        /// 构造流式事件
        /// </summary>
        /// <param name="kind">事件种类</param>
        /// <param name="text">增量文本或错误文本</param>
        public LlmStreamEvent(LlmStreamKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }
}
