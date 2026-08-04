namespace Mau.Runtime
{
    /// <summary>
    /// 生成物接口——宿主驱动的契约
    /// </summary>
    public interface IFlow
    {
        /// <summary>
        /// 每帧驱动——宿主主循环调用
        /// </summary>
        void Tick();
    }
}
