namespace Mau.Runtime
{
    /// <summary>
    /// 变迁快照——RuntimeStatus 的子项
    /// </summary>
    public readonly struct TransSnapshot
    {
        /// <summary>
        /// 变迁名——T_ 前缀
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Cube 状态——Idle/Running/Expired。无 Cube 的变迁始终为 Idle
        /// </summary>
        public readonly string CubeState;

        /// <summary>
        /// 已推进帧数——Cube 已消耗帧数，无 Cube 为 0
        /// </summary>
        public readonly long ElapsedFrames;

        /// <summary>
        /// 时限帧数——Cube 上限，无 Cube 为 0
        /// </summary>
        public readonly long LimitFrames;

        /// <summary>
        /// 构造变迁快照
        /// </summary>
        /// <param name="name">变迁名</param>
        /// <param name="cubeState">Cube 状态</param>
        /// <param name="elapsedFrames">已用帧数</param>
        /// <param name="limitFrames">时限帧数</param>
        public TransSnapshot(string name, string cubeState, long elapsedFrames, long limitFrames)
        {
            Name = name;
            CubeState = cubeState;
            ElapsedFrames = elapsedFrames;
            LimitFrames = limitFrames;
        }
    }
}
