namespace CatHome4.Contracts
{
    /// <summary>
    /// 宿主推送面——会话 → 外观层的 SSE 事件出口。
    /// Core 域消费（ChatSession 推送），Http 域实现（HttpHost SSE 广播）。
    /// 拆分前 ChatSession 直接持有 HttpHost 具体类（AttachHost 注入）；拆分后解耦为接口注入。
    /// </summary>
    public interface IHostPush
    {
        /// <summary>
        /// 视图事件推送——视图出口统一 op 面（A158 期三：两区镜像）。
        /// op 取值：persist.append（持久块建块即推）· live.add / live.update / live.remove（流式区镜像）
        /// · control（瞬时事件面：usage / paused / session_reset——chatdone / note 已于 A191 退役）。
        /// </summary>
        /// <param name="op">出口事件类型——persist.append / live.add / live.update / live.remove / control</param>
        /// <param name="payload">载荷 JSON 字符串（live.remove 为空串）</param>
        /// <param name="meta">块元数据 JSON（A158 块字段：key / renderType / ts / durMs / state / id / src / origin——后端自述，前端按契约消费；空串 = 不带）</param>
        void PushView(string op, string payload, string meta);
    }
}
