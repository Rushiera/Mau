using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// OA 工单。挂单方投递到 OA 的不可变描述 + OA 维护的可变状态。
    /// 载荷为双字典（OfficeData）——int/str 两通道按 Key 存取（design-mau-module.md §二）。
    /// </summary>
    public struct Office
    {
        /// <summary>
        /// OA 内自增唯一 ID
        /// </summary>
        public long OfficeId;

        /// <summary>
        /// 工单大类——"OI_IO" / "OI_TCP" ...（v3 纯化：OI_LLM 随 LLM 组件退役）
        /// </summary>
        public string OfficeType;

        /// <summary>
        /// 固定词汇——执行方据此判断自己能不能干
        /// </summary>
        public string OfficeName;

        /// <summary>
        /// 所有者（挂单方）的 LongId
        /// </summary>
        public long OwnerId;

        /// <summary>
        /// 请求载荷——双字典（Dog 按 Key 写，Cat 按 Key 读）
        /// </summary>
        public OfficeData Data;

        /// <summary>
        /// 当前状态
        /// </summary>
        public OfficeState Status;

        /// <summary>
        /// 认领者 LongId（0=未认领）
        /// </summary>
        public long ClaimByWorkerId;

        /// <summary>
        /// 上架帧号
        /// </summary>
        public long PostFrame;

        /// <summary>
        /// 认领帧号
        /// </summary>
        public long ClaimFrame;

        /// <summary>
        /// 挂单方设定的超时帧数
        /// </summary>
        public long TimeoutFrames;

        /// <summary>
        /// 回执载荷——双字典（Closed 后有效，Cat 按 Key 写）
        /// </summary>
        public OfficeData Result;
    }
}
