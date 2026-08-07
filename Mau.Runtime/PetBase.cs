namespace Mau.Runtime
{
    /// <summary>
    /// 常驻服务基类——IPet 默认实现（空 Tick，子类覆写）。
    /// 基座三分类之 Pet：无业务主体、跨流程常驻（视图/监视/心跳）。注册进 FlowRunner 全局 Tick 驱动。
    /// </summary>
    public class PetBase : IPet
    {
        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        private long _petId;

        /// <summary>
        /// Pet 名字
        /// </summary>
        private string _petName;

        /// <summary>
        /// Flow 注册 ID
        /// </summary>
        public long PetId
        {
            get { return _petId; }
        }

        /// <summary>
        /// Pet 名字
        /// </summary>
        public string PetName
        {
            get { return _petName; }
        }

        /// <summary>
        /// Pet 类型名——子类覆写
        /// </summary>
        public virtual string PetType
        {
            get { return "PetBase"; }
        }

        /// <summary>
        /// 构造常驻服务基类
        /// </summary>
        /// <param name="name">Pet 名字</param>
        public PetBase(string name)
        {
            _petName = name ?? "";
        }

        /// <summary>
        /// 绑定注册 ID
        /// </summary>
        /// <param name="petId">Flow 注册 ID</param>
        public void BindId(long petId)
        {
            _petId = petId;
        }

        /// <summary>
        /// IFlow.Tick——全局帧序驱动（常驻服务每帧逻辑，子类覆写）
        /// </summary>
        public virtual void Tick()
        {
        }
    }
}
