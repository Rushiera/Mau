using System;

namespace Mau.Runtime
{
    /// <summary>
    /// 多 Flow 统合快照——RuntimeObserver 的推送给 Transport 的数据单元
    /// </summary>
    public sealed class AggregatedSnapshot
    {
        /// <summary>
        /// 快照时间戳（UTC）
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 宿主帧号
        /// </summary>
        public long HostFrame { get; set; }

        /// <summary>
        /// 各 Flow 的状态快照
        /// </summary>
        public FlowSnapshotEntry[] Flows { get; set; }

        /// <summary>
        /// 系统级事件——故障、预算告警等
        /// </summary>
        public SystemEvent[] SystemEvents { get; set; }

        /// <summary>
        /// 构造统合快照
        /// </summary>
        public AggregatedSnapshot()
        {
            Timestamp = DateTime.UtcNow;
            HostFrame = 0;
            Flows = Array.Empty<FlowSnapshotEntry>();
            SystemEvents = Array.Empty<SystemEvent>();
        }
    }

    /// <summary>
    /// 单个 Flow 的快照条目
    /// </summary>
    public sealed class FlowSnapshotEntry
    {
        /// <summary>
        /// Flow 标识名（类名）
        /// </summary>
        public string FlowName { get; set; }

        /// <summary>
        /// 运行时状态
        /// </summary>
        public RuntimeStatus Status { get; set; }

        /// <summary>
        /// 调试日志
        /// </summary>
        public MauDebug[] Logs { get; set; }

        /// <summary>
        /// 构造
        /// </summary>
        public FlowSnapshotEntry()
        {
            FlowName = "";
            Status = default;
            Logs = Array.Empty<MauDebug>();
        }
    }
}
