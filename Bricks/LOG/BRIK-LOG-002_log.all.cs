// ═══════════════════════════════════════════════════
// 积木: log.all
// ID:   BRIK-LOG-002
// 类别: LOG
// 作用: 读取全部日志（文本行格式：时间 | 模块 | 级别 | 消息）
// 依赖: 无
// 引用: System.Text
// 原理: 锁内遍历静态总账拼接文本行
// 常用: 调试面板 / 日志导出
// ═══════════════════════════════════════════════════
using System.Text;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.all 读取全部日志（依赖 LogStore）
    /// </summary>
    public static class LogAllBrick
    {
        /// <summary>
        /// 读取全部日志
        /// </summary>
        /// <param name="text">日志文本行</param>
        /// <returns>true=成功</returns>
        public static bool GetAll(out string text)
        {
            StringBuilder sb = new StringBuilder();
            lock (LogStore.Sync)
            {
                for (int i = 0; i < LogStore.AllLog.Count; i = i + 1)
                {
                    LogStore.LogEntry entry = LogStore.AllLog[i];
                    sb.Append(entry.Time).Append(" | ").Append(entry.Module)
                        .Append(" | ").Append(LogStore.LevelText(entry.Level))
                        .Append(" | ").Append(entry.Message).Append('\n');
                }
            }
            text = sb.ToString();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:CFA9236B8138F2CCC23F6C4395EC9B8A5FDA72C16FF8A8B36B3949531F37AD44
