namespace CatHome4.Contracts
{
    /// <summary>
    /// 视图块序变更——合并视图数组坐标下的最小差异（A111）。
    /// 发起方：视图层（SessionViewStore）——清除 / 重建 / 轮统计清理 / 区间转废弃四处变更点统一出声；
    /// 消费方：QQ 转发面（订阅通知按区间平移或锚点重定位校正块游标）。
    /// 坐标口径：变更前合并数组（GetBlocks 序）下标；同一位置「移除 N 块 + 新增 M 块」为一次变更。
    /// </summary>
    public sealed class ViewOrderChange
    {
        /// <summary>变更起点——区间首块下标（变更前合并数组坐标）</summary>
        public int From;

        /// <summary>被移除块数——区间内消失的块数</summary>
        public int RemovedCount;

        /// <summary>新增块数——插在同一位置的新块数（如 timeback 回收的合并归档块）</summary>
        public int AddedCount;
    }
}
