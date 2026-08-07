namespace Mau.Runtime
{
    /// <summary>
    /// 常驻辅助服务接口——基座三分类之 Pet（Cat 主动流程 / Dog 工单载体 / Pet 常驻服务）。
    /// Pet 无业务主体：视图/监视/心跳等跨流程常驻逻辑。实现 IFlow——注册进 FlowRunner 由全局 Tick 驱动。
    /// </summary>
    public interface IPet : IFlow
    {
        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        long PetId { get; }

        /// <summary>
        /// Pet 名字
        /// </summary>
        string PetName { get; }

        /// <summary>
        /// Pet 类型名
        /// </summary>
        string PetType { get; }

        /// <summary>
        /// 绑定注册 ID——注册后由宿主/工厂调用
        /// </summary>
        /// <param name="petId">Flow 注册 ID</param>
        void BindId(long petId);
    }
}
