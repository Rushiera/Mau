namespace Mau.Runtime
{
    /// <summary>
    /// 观察器传输接口——基座推送到外部消费者的抽象
    /// 实现：HttpTransport（Mau.Observer）/ NamedPipeTransport / GodotTransport
    /// </summary>
    public interface IObserverTransport
    {
        /// <summary>
        /// 推送统合快照
        /// </summary>
        /// <param name="snapshot">多 Flow 统合状态</param>
        void Push(AggregatedSnapshot snapshot);
    }
}
