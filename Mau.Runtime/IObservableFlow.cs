namespace Mau.Runtime
{
    /// <summary>
    /// 可观察生成物接口——IFlow + 状态快照 + 调试日志
    /// 翻译器强制生成此接口的实现
    /// </summary>
    public interface IObservableFlow : IFlow
    {
        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        RuntimeStatus GetStatus();

        /// <summary>
        /// 获取全量调试日志
        /// </summary>
        /// <returns>日志数组，时间顺序</returns>
        MauDebug[] GetLogs();
    }
}
