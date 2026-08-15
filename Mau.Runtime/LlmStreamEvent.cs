using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 流式事件种类——P4 最小面：思考/回复双通道 + 结束。
    /// Text/Reasoning = 增量事件；Done = 流正常结束（[DONE] 到达）；Error = 失败终止。
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
        /// 流正常结束（[DONE] 到达）——Text 为空
        /// </summary>
        Done,

        /// <summary>
        /// 失败终止——Text 携带 ERR|码|详情（错误可见性：禁止吞错）
        /// </summary>
        Error
    }

    /// <summary>
    /// 流式事件——增量/结束/错误统一载体（不可变）。
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
        /// 增量文本或错误文本（Done 时为空）
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
