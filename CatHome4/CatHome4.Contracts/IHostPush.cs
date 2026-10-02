namespace CatHome4.Contracts
{
    /// <summary>
    /// 宿主推送面——会话 → 外观层的 SSE 事件出口。
    /// Core 域消费（ChatSession 推送），Http 域实现（HttpHost SSE 广播）。
    /// 拆分前 ChatSession 直接持有 HttpHost 具体类（AttachHost 注入）；拆分后解耦为接口注入。
    /// </summary>
    public interface IHostPush
    {
        /// <summary>会话完成事件——chatdone（前端阶段封口）</summary>
        /// <param name="count">会话消息数</param>
        void PushChatDone(int count);

        /// <summary>Note 状态事件——前端悬浮气泡实时重绘</summary>
        /// <param name="json">Note 状态 JSON</param>
        void PushNoteState(string json);

        /// <summary>视图事件推送——F4 视图块统一出口（流式增量/整块/控制块）</summary>
        /// <param name="renderType">渲染类型——stream/user/text/reason/toolcard/control</param>
        /// <param name="payload">载荷 JSON 字符串</param>
        /// <param name="replaceSeq">被替换块序号（流式→整块替换；-1=无替换）</param>
        /// <param name="seqHint">流式增量带已分配序号（&gt;0 不递增；≤0 分配新序号并返回）</param>
        /// <returns>事件序号（流式容器标识）</returns>
        int PushView(string renderType, string payload, long replaceSeq, long seqHint);
    }
}
