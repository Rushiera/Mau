namespace Mau.Runtime
{
    /// <summary>
    /// 常驻辅助服务接口——基座三分类之 Pet（Cat 主动流程 / Dog 工单载体 / Pet 常驻服务）。
    /// Pet 无业务主体：视图/监视/心跳等跨流程常驻逻辑。实现 IFlow——注册进 FlowRunner 由全局 Tick 驱动。
    /// 接口成员提供默认实现（DIM）——翻译器生成物声明实现本接口时无需手写成员（Tick 由 IObservableFlow 提供）。
    /// </summary>
    public interface IPet : IFlow
    {
        /// <summary>
        /// Flow 注册 ID——默认 0（BindId 由宿主绑定）
        /// </summary>
        long PetId
        {
            get { return 0; }
        }

        /// <summary>
        /// Pet 名字——默认空（宿主/工厂设置）
        /// </summary>
        string PetName
        {
            get { return ""; }
        }

        /// <summary>
        /// Pet 类型名——默认类名
        /// </summary>
        string PetType
        {
            get { return GetType().Name; }
        }

        /// <summary>
        /// 绑定注册 ID——注册后由宿主/工厂调用（默认空实现）
        /// </summary>
        /// <param name="petId">Flow 注册 ID</param>
        void BindId(long petId)
        {
        }
    }
}
