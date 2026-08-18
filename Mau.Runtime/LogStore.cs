using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 日志总账（程序级）——状态经 DataBox scope 存储（BRIK 唯一数据协议）
    /// </summary>
    public static class LogStore
    {
        /// <summary>
        /// 日志条目类型——已归 Mau.Runtime
        /// </summary>
        public struct LogEntry
        {
            /// <summary>
            /// 时间戳
            /// </summary>
            public string Time;

            /// <summary>
            /// 模块名
            /// </summary>
            public string Module;

            /// <summary>
            /// 级别——0=INFO 2=WARN 3=ERROR
            /// </summary>
            public int Level;

            /// <summary>
            /// 消息
            /// </summary>
            public string Message;

            /// <summary>
            /// 类别——""=普通 / "CMD"=Command 总线（C 类）/ "OA"=工单系统（O 类）——INFO 分支（2026-08-10）
            /// </summary>
            public string Category;

            /// <summary>
            /// 帧号——写入时全局帧号（FlowRunner 驱动；-1=无帧号来源）
            /// </summary>
            public long Frame;
        }

        /// <summary>
        /// 总账列表——DataBox scope "log"
        /// </summary>
        public static List<LogEntry> AllLog
        {
            get { return DataBox.GetOrCreate<List<LogEntry>>("log", "entries"); }
        }

        /// <summary>
        /// 总账锁——DataBox scope "log"
        /// </summary>
        public static object Sync
        {
            get { return DataBox.GetOrCreate<object>("log", "sync"); }
        }

        /// <summary>
        /// 磁盘持久化路径——空=仅内存
        /// </summary>
        public static string LogFilePath
        {
            get
            {
                string path;
                if (DataBox.TryGet<string>("log", "filePath", out path))
                {
                    return path;
                }
                return "";
            }
            set { DataBox.Set<string>("log", "filePath", value); }
        }

        /// <summary>
        /// 磁盘写入器——常驻 StreamWriter（D5：告别逐条 AppendAllText 反复开文件；1s 定时 flush）
        /// </summary>
        private static System.IO.StreamWriter? _writer = null;

        /// <summary>
        /// 写入器当前路径——路径变化时重开
        /// </summary>
        private static string _writerPath = "";

        /// <summary>
        /// 写盘锁——并发 Add 串行化
        /// </summary>
        private static readonly object _writeGate = new object();

        /// <summary>
        /// 上次 flush 时间——1s 定时刷
        /// </summary>
        private static DateTime _lastFlush = DateTime.MinValue;

        /// <summary>
        /// 配置磁盘日志文件——宿主启动时调用
        /// </summary>
        /// <param name="path">日志文件路径，空=仅内存</param>
        public static void ConfigureLogFile(string path)
        {
            LogFilePath = path;
            if (LogFilePath == null)
            {
                LogFilePath = "";
            }
        }

        /// <summary>
        /// 写入一条结构化日志（内存总账 + 可选磁盘持久化）——C/O 类机制埋点统一入口（2026-08-10）
        /// </summary>
        /// <param name="module">模块名</param>
        /// <param name="level">级别——0=INFO 2=WARN 3=ERROR</param>
        /// <param name="message">消息</param>
        /// <param name="category">类别——""/CMD/OA（INFO 分支）</param>
        public static void Add(string module, int level, string message, string category)
{
            LogEntry entry;
            entry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.Module = SafeText(module);
            entry.Level = level;
            entry.Message = SafeText(message);
            entry.Category = category;
            if (entry.Category == null)
            {
                entry.Category = "";
            }
            entry.Frame = FlowRunner.GlobalFrame;
            lock (Sync)
            {
                AllLog.Add(entry);
            }
            if (LogFilePath.Length > 0)
            {
                try
                {
                    // 落盘行——类别段 C/O 类保留 [{category}]；帧号段 F{frame}（P3c 观测全链——落盘帧号可回溯）
                    string line = entry.Time + " | F" + entry.Frame + " | " + entry.Module + " | "
                        + LevelText(level) + " | " + entry.Message;
                    if (entry.Category.Length > 0)
                    {
                        line = entry.Time + " | F" + entry.Frame + " | " + entry.Module + " | "
                            + LevelText(level) + " | [" + entry.Category + "] | " + entry.Message;
                    }
                    // 常驻写入器——1s 定时 flush（D5：缓冲化，避免逐条开文件同步 IO）
                    lock (_writeGate)
                    {
                        if (_writer == null || _writerPath != LogFilePath)
                        {
                            if (_writer != null)
                            {
                                try
                                {
                                    _writer.Flush();
                                }
                                catch
                                {
                                }
                                _writer.Dispose();
                                _writer = null;
                            }
                            string? dir = System.IO.Path.GetDirectoryName(LogFilePath);
                            if (dir != null && dir.Length > 0)
                            {
                                System.IO.Directory.CreateDirectory(dir);
                            }
                            _writerPath = LogFilePath;
                            _writer = new System.IO.StreamWriter(new System.IO.FileStream(LogFilePath, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite), new System.Text.UTF8Encoding(false));
                        }
                        _writer.WriteLine(line);
                        DateTime now = DateTime.Now;
                        if ((now - _lastFlush).TotalSeconds >= 1.0)
                        {
                            _writer.Flush();
                            _lastFlush = now;
                        }
                    }
                }
                catch
                {
                    // 磁盘写失败——内存总账已保留
                }
            }
        }
        /// <summary>
        /// 级别文本
        /// </summary>
        /// <param name="level">级别数字</param>
        /// <returns>文本</returns>
        public static string LevelText(int level)
        {
            if (level >= 3)
            {
                return "ERROR";
            }
            if (level == 2)
            {
                return "WARN";
            }
            return "INFO";
        }

        /// <summary>
        /// 把可空文本规范为空字符串
        /// </summary>
        /// <param name="value">输入</param>
        /// <returns>非空文本</returns>
        public static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }
}
