// ═══════════════════════════════════════════════════
// 积木: log.write
// ID:   BRIK-LOG-001
// 类别: LOG
// 作用: 写入一条结构化日志（内存总账 + 可选磁盘持久化）
// 依赖: 无
// 引用: System · System.IO · System.Text
// 原理: 构造条目 → 锁内入总账 → 配置了磁盘路径则追加写文件
// 常用: CH4 日志系统 / 调试面板 / 错误追踪
// ═══════════════════════════════════════════════════
using System;
using System.IO;
using System.Text;

using Mau.Runtime;
namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.write 写入一条结构化日志（依赖 LogStore）
    /// </summary>
    public static class LogWriteBrick
    {
        /// <summary>
        /// 写入一条日志
        /// </summary>
        /// <param name="module">模块名</param>
        /// <param name="level">级别——0=INFO 2=WARN 3=ERROR</param>
        /// <param name="message">消息</param>
        /// <returns>true=成功</returns>
        public static bool Write(string module, int level, string message)
        {
            LogStore.LogEntry entry;
            entry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.Module = LogStore.SafeText(module);
            entry.Level = level;
            entry.Message = LogStore.SafeText(message);
            entry.Frame = FlowRunner.GlobalFrame;
            lock (LogStore.Sync)
            {
                LogStore.AllLog.Add(entry);
            }
            if (LogStore.LogFilePath.Length > 0)
            {
                try
                {
                    string line = entry.Time + " | " + entry.Module + " | "
                        + LogStore.LevelText(level) + " | " + entry.Message;
                    File.AppendAllText(LogStore.LogFilePath, line + "\n",
                        new UTF8Encoding(false));
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }
    }
}
// #MAU_CHECKSUM:SHA256:3D7EA4DA56CB2C122741DF481F16D7F8F453DEAC85AE0819B29034521F62A371
