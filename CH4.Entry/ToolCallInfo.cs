namespace CH4
{
    /// <summary>
    /// 待回传工具调用信息——LLM tool_calls 解析结果与工具执行结果的配对载体。
    /// </summary>
    public sealed class ToolCallInfo
    {
        /// <summary>
        /// 调用 ID（tool_call_id——OpenAI wire）
        /// </summary>
        public string Id;

        /// <summary>
        /// 工具名（read_file / ask——宿主工具 schema 声明）
        /// </summary>
        public string Name;

        /// <summary>
        /// 参数 JSON（arguments——完整 JSON 文本）
        /// </summary>
        public string Arguments;

        /// <summary>
        /// 建立待回传工具调用
        /// </summary>
        /// <param name="id">调用 ID</param>
        /// <param name="name">工具名</param>
        /// <param name="arguments">参数 JSON</param>
        public ToolCallInfo(string id, string name, string arguments)
        {
            Id = id;
            Name = name;
            Arguments = arguments;
        }
    }
}
