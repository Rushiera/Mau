namespace Mau.Runtime
{
    /// <summary>
    /// 系统通知服务——工具面向指定猫会话投递一条系统注入消息（"发声"通道）。
    /// 宿主注入实现（Program 组合根绑定——等价 ChatBridge.PostSystemMessage(catKey, "systemauto", content)：
    /// 前台可见的系统块、随轮次消费；忙时排队，不打断在途轮次）。
    /// 语料经 DataBox.TryResolve 触达——现行消费：web.search 验证升级（搜索通道命中验证码时通知人工处理）。
    /// </summary>
    public interface ISystemNotifyService
    {
        /// <summary>
        /// 投递系统通知——失败可见（OK 受理 / ERR|CODE|msg），不静默丢弃。
        /// </summary>
        /// <param name="catKey">目标猫（会话标识）</param>
        /// <param name="content">通知内容（渠道前缀由调用方自带）</param>
        /// <returns>内核回执原文</returns>
        string Notify(string catKey, string content);
    }
}
