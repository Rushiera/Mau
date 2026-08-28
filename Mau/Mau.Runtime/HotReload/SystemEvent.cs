using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 系统级事件——故障、预算告警、启动错误等
    /// </summary>
    public sealed class SystemEvent
    {
        /// <summary>
        /// 事件发生时的宿主帧号
        /// </summary>
        public long HostFrame { get; set; }

        /// <summary>
        /// 事件时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 事件级别——Warn / Error
        /// </summary>
        public string Level { get; set; }

        /// <summary>
        /// 事件来源——FlowName / "Host" / "Observer" / "Transport"
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// 事件消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 构造系统事件
        /// </summary>
        public SystemEvent()
        {
            HostFrame = 0;
            Timestamp = DateTime.UtcNow;
            Level = "Warn";
            Source = "";
            Message = "";
        }
    }
}
