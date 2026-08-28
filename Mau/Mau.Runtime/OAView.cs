namespace Mau.Runtime
{
    /// <summary>
    /// OA 工单域在一次 Tick 结算后的数量与版本摘要——透明度暴露
    /// </summary>
    public struct OAView
    {
        /// <summary>
        /// OA 域变化版本
        /// </summary>
        public long Version;

        /// <summary>
        /// 等待认领的工单数
        /// </summary>
        public int OpenCount;

        /// <summary>
        /// 已经认领且执行中的工单数
        /// </summary>
        public int WorkCount;

        /// <summary>
        /// 已经正常完成的工单数
        /// </summary>
        public int ClosedCount;

        /// <summary>
        /// 已经超时的工单数
        /// </summary>
        public int TimeoutCount;
    }
}
