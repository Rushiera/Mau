using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 宿主机制快照——OA/Command/ID/线程守卫的统一观察截面，透明度暴露
    /// 由 RuntimeObserver 在 Tick 时从宿主机制拉取，聚合进 AggregatedSnapshot
    /// </summary>
    public sealed class HostSnapshot
    {
        /// <summary>
        /// 快照宿主帧号
        /// </summary>
        public long Frame;

        /// <summary>
        /// 当前是否宿主主线程
        /// </summary>
        public bool IsMainThread;

        /// <summary>
        /// OA 工单快照——未注入时为 null
        /// </summary>
        public OAView? OA;

        /// <summary>
        /// Command 指令总线快照——未注入时为 null
        /// </summary>
        public CommandSnapshot? Command;

        /// <summary>
        /// ID 类型累计计数快照——未注入时为 null
        /// </summary>
        public IReadOnlyDictionary<string, int>? IdTypeCounts;

        /// <summary>
        /// 下一个可分配的全局 ID——未注入时为 0
        /// </summary>
        public long NextId;

        /// <summary>
        /// 构造空宿主快照
        /// </summary>
        public HostSnapshot()
        {
            Frame = 0;
            IsMainThread = true;
            OA = null;
            Command = null;
            IdTypeCounts = null;
            NextId = 0;
        }
    }
}
