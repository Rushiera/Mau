namespace CatHome4.Contracts
{
    /// <summary>
    /// 宿主推送面——会话 → 外观层的 SSE 事件出口。
    /// Core 域消费（ChatSession 推送），Http 域实现（HttpHost SSE 广播）。
    /// 拆分前 ChatSession 直接持有 HttpHost 具体类（AttachHost 注入）；拆分后解耦为接口注入。
    /// </summary>
    public interface IHostPush
    {
        /// <summary>用户消息事件——所有进内核的消息统一出口（单向数据流：前端气泡唯一来源）</summary>
        /// <param name="text">消息文本</param>
        /// <param name="source">来源（user/qq 等）</param>
        void PushUserMessage(string text, string source);

        /// <summary>LLM 流式事件转发——kind 五态（text/reasoning/toolCalls/done/error）+ sessionId 归属</summary>
        /// <param name="kind">事件类型</param>
        /// <param name="text">载荷</param>
        void PushLlm(string kind, string text);

        /// <summary>工具结果实时推送——tool 事件（前端工具卡）</summary>
        /// <param name="name">工具名</param>
        /// <param name="arguments">参数 JSON（截断）</param>
        /// <param name="result">结果（截断）</param>
        void PushToolResult(string name, string arguments, string result);

        /// <summary>会话完成事件——chatdone（前端阶段封口）</summary>
        /// <param name="count">会话消息数</param>
        void PushChatDone(int count);

        /// <summary>Note 状态事件——前端悬浮气泡实时重绘</summary>
        /// <param name="json">Note 状态 JSON</param>
        void PushNoteState(string json);
    }
}
