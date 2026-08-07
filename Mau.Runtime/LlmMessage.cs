namespace Mau.Runtime
{
    /// <summary>
    /// 厂商无关 LLM 消息（契约类型——上下文存储的消息单元）
    /// </summary>
    public struct LlmMessage
    {
        /// <summary>
        /// 角色——System/User/Assistant/Tool
        /// </summary>
        public string Role;

        /// <summary>
        /// 正文
        /// </summary>
        public string Content;

        /// <summary>
        /// 工具调用 ID——Tool 消息必填
        /// </summary>
        public string ToolCallId;

        /// <summary>
        /// 工具名
        /// </summary>
        public string ToolName;

        /// <summary>
        /// 工具调用 JSON 数组文本——Assistant 工具声明
        /// </summary>
        public string ToolCallsJson;
    }
}
