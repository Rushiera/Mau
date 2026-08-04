namespace Mau.Runtime
{
    /// <summary>
    /// 运行时状态快照——每帧期末的完整截面，供外部观察者拉取
    /// </summary>
    public readonly struct RuntimeStatus
    {
        /// <summary>
        /// 快照帧号
        /// </summary>
        public readonly long Frame;

        /// <summary>
        /// 命题状态列表——名称/类型/当前值
        /// </summary>
        public readonly PropSnapshot[] Propositions;

        /// <summary>
        /// 变迁状态列表——Cube 状态/已用帧/时限
        /// </summary>
        public readonly TransSnapshot[] Transitions;

        /// <summary>
        /// 资源状态列表——独占标志/配额
        /// </summary>
        public readonly ResSnapshot[] Resources;

        /// <summary>
        /// 构造运行时状态快照
        /// </summary>
        /// <param name="frame">帧号</param>
        /// <param name="propositions">命题快照</param>
        /// <param name="transitions">变迁快照</param>
        /// <param name="resources">资源快照</param>
        public RuntimeStatus(long frame, PropSnapshot[] propositions, TransSnapshot[] transitions, ResSnapshot[] resources)
        {
            Frame = frame;
            Propositions = propositions;
            Transitions = transitions;
            Resources = resources;
        }
    }
}
