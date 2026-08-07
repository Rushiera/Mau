// ═══════════════════════════════════════════════════
// 积木: log.clear
// ID:   BRIK-LOG-004
// 类别: LOG
// 作用: 清空内存总账
// 依赖: 无
// 引用: 无
// 原理: 锁内 Clear 静态总账列表
// 常用: 会话开始前重置 / 测试隔离
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.clear 清空内存总账（依赖 LogStore）
    /// </summary>
    public static class LogClearBrick
    {
        /// <summary>
        /// 清空内存总账
        /// </summary>
        /// <returns>true=成功</returns>
        public static bool Clear()
        {
            lock (LogStore.Sync)
            {
                LogStore.AllLog.Clear();
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:1DE7BB01954D5B85790D772310380357A6AA7D3FD3D37DC1C47EC6FC365DA806
