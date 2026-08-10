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
                    // 类别段——C/O 类输出保留类别（G2 覆盖）
                    string cat = "";
                    if (entry.Category.Length > 0)
                    {
                        cat = " | [" + entry.Category + "]";
                    }
                    sb.Append(entry.Time).Append(" | ").Append(entry.Module)
                        .Append(" | ").Append(LogStore.LevelText(entry.Level))
                        .Append(cat)
                        .Append(" | ").Append(entry.Message).Append('\n');
                }
            }
            text = sb.ToString();
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:7BF978D2478B47F3353BDF79CE805CE96334AEC4972B4DBAD823C18D6EAA33AB
