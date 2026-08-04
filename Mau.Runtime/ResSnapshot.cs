namespace Mau.Runtime
{
    /// <summary>
    /// 资源快照——RuntimeStatus 的子项
    /// </summary>
    public readonly struct ResSnapshot
    {
        /// <summary>
        /// 资源名——R_ 前缀
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 是否独占资源
        /// </summary>
        public readonly bool IsExclusive;

        /// <summary>
        /// 当前配额占用数——非配额资源为 0
        /// </summary>
        public readonly int QuotaCurrent;

        /// <summary>
        /// 配额上限——非配额资源为 0
        /// </summary>
        public readonly int QuotaMax;

        /// <summary>
        /// 构造资源快照
        /// </summary>
        /// <param name="name">资源名</param>
        /// <param name="isExclusive">是否独占</param>
        /// <param name="quotaCurrent">当前占用</param>
        /// <param name="quotaMax">配额上限</param>
        public ResSnapshot(string name, bool isExclusive, int quotaCurrent, int quotaMax)
        {
            Name = name;
            IsExclusive = isExclusive;
            QuotaCurrent = quotaCurrent;
            QuotaMax = quotaMax;
        }
    }
}
