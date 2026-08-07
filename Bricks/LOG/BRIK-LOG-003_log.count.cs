// ═══════════════════════════════════════════════════
// 积木: log.count
// ID:   BRIK-LOG-003
// 类别: LOG
// 作用: 返回日志总账条数
// 依赖: 无
// 引用: 无
// 原理: 锁内读取静态总账 Count
// 常用: 日志规模统计
// ═══════════════════════════════════════════════════

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.count 返回日志总账条数（依赖 LogStore）
    /// </summary>
    public static class LogCountBrick
    {
        /// <summary>
        /// 日志条数
        /// </summary>
        /// <param name="count">条数</param>
        /// <returns>true=成功</returns>
        public static bool Count(out int count)
        {
            lock (LogStore.Sync)
            {
                count = LogStore.AllLog.Count;
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:C0F5B5908EA37538F8CE27BE09564BBEB43315AA5FF9CDABF8378BF9D5C9E7E1
