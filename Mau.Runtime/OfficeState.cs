namespace Mau.Runtime
{
    /// <summary>
    /// OA 工单状态——从挂单到终态的四阶段。
    /// 没有 Failed 态——执行方干不了走 Relist 变回 Open，不给回执走 TimeOut。
    /// </summary>
    public enum OfficeState
    {
        /// <summary>
        /// 已挂单，等待执行方取走
        /// </summary>
        Open,

        /// <summary>
        /// 已被执行方取走，执行中
        /// </summary>
        Work,

        /// <summary>
        /// 完成，结果已写回
        /// </summary>
        Closed,

        /// <summary>
        /// 超时——Open 或 Work 超过时限
        /// </summary>
        TimeOut
    }
}
