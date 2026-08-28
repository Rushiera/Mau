namespace Mau.Runtime
{
    /// <summary>
    /// 消息角色——OpenAI 兼容四 role（system/user/assistant/tool）
    /// </summary>
    public enum LlmRole
    {
        /// <summary>
        /// 系统提示词
        /// </summary>
        System,

        /// <summary>
        /// 用户消息
        /// </summary>
        User,

        /// <summary>
        /// 助手消息（可携带 tool_calls / reasoning_content 回传）
        /// </summary>
        Assistant,

        /// <summary>
        /// 工具结果（必须携带 ToolCallId）
        /// </summary>
        Tool
    }

    /// <summary>
    /// 厂商无关单条 LLM 消息——OpenAI 兼容消息序列的元素。
    /// ToolCallsJson 为 assistant 工具调用 JSON 数组原样存放；ReasoningContent 思考内容（工具调用轮次必须回传）。
    /// </summary>
    public struct LlmMessage
    {
        /// <summary>
        /// 消息角色
        /// </summary>
        public LlmRole Role;

        /// <summary>
        /// 消息正文；无正文时为空字符串
        /// </summary>
        public string Content;

        /// <summary>
        /// 工具结果对应的调用 ID（Role=Tool 时有效）
        /// </summary>
        public string ToolCallId;

        /// <summary>
        /// 工具名称（Role=Tool 时有效）
        /// </summary>
        public string ToolName;

        /// <summary>
        /// assistant 工具调用 JSON 数组（Role=Assistant 时有效——OpenAI wire tool_calls 原样）
        /// </summary>
        public string ToolCallsJson;

        /// <summary>
        /// assistant 思考内容（reasoning_content——有工具调用轮次必须回传；无工具调用时可为空串）
        /// </summary>
        public string ReasoningContent;
    }
}
