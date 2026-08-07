// ═══════════════════════════════════════════════
// 积木: log.write / log.all / log.clear
// ID:   BRIK-LOG-001 ~ 003
// 作用: 总账式结构化日志——静态内存总账 + 可选磁盘持久化
// 引用: Mau.Bricks.Log → Mau.Contracts（BrickRegistry）· System.IO
// 依赖: 无
// 原理: 静态总账列表 + 按模块分索引——持久化 AllLog/ErrLog 文件
// 常用: CH4 日志系统 / 调试面板 / 错误追踪
// ═══════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mau.Contracts;

namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——标准积木库日志类。总账模式——全部日志存储在静态列表中。
    /// 由 CH2 CH_Tool_Log 移植（简化：内存总账 + 可选磁盘）。
    /// </summary>
    public static class LogBrick
    {
        /// <summary>
        /// 日志条目
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
        }

        /// <summary>
        /// 静态总账
        /// </summary>
        private static readonly List<LogEntry> _allLog = new List<LogEntry>();

        /// <summary>
        /// 磁盘持久化路径——ConfigureLogFile 配置；空=仅内存
        /// </summary>
        private static string _logFilePath = "";

        /// <summary>
        /// 配置磁盘日志文件
        /// </summary>
        /// <param name="path">日志文件路径，空=仅内存</param>
        public static void ConfigureLogFile(string path)
        {
            _logFilePath = path == null ? "" : path;
        }

        /// <summary>
        /// 写入一条日志
        /// </summary>
        /// <param name="module">模块名</param>
        /// <param name="level">级别——0=INFO 2=WARN 3=ERROR</param>
        /// <param name="message">消息</param>
        /// <returns>true=成功</returns>
        public static bool Write(string module, int level, string message)
        {
            LogEntry entry;
            entry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.Module = SafeText(module);
            entry.Level = level;
            entry.Message = SafeText(message);
            lock (_allLog)
            {
                _allLog.Add(entry);
            }
            if (_logFilePath.Length > 0)
            {
                try
                {
                    string line = entry.Time + " | " + entry.Module + " | "
                        + LevelText(level) + " | " + entry.Message;
                    File.AppendAllText(_logFilePath, line + "\n",
                        new UTF8Encoding(false));
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 读取全部日志
        /// </summary>
        /// <param name="text">日志文本行</param>
        /// <returns>true=成功</returns>
        public static bool GetAll(out string text)
        {
            StringBuilder sb = new StringBuilder();
            lock (_allLog)
            {
                for (int i = 0; i < _allLog.Count; i = i + 1)
                {
                    LogEntry entry = _allLog[i];
                    sb.Append(entry.Time).Append(" | ").Append(entry.Module)
                        .Append(" | ").Append(LevelText(entry.Level))
                        .Append(" | ").Append(entry.Message).Append('\n');
                }
            }
            text = sb.ToString();
            return true;
        }

        /// <summary>
        /// 日志条数
        /// </summary>
        /// <param name="count">条数</param>
        /// <returns>true=成功</returns>
        public static bool Count(out int count)
        {
            lock (_allLog)
            {
                count = _allLog.Count;
            }
            return true;
        }

        /// <summary>
        /// 清空内存总账
        /// </summary>
        /// <returns>true=成功</returns>
        public static bool Clear()
        {
            lock (_allLog)
            {
                _allLog.Clear();
            }
            return true;
        }

        /// <summary>
        /// 级别文本
        /// </summary>
        /// <param name="level">级别数字</param>
        /// <returns>文本</returns>
        private static string LevelText(int level)
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
        private static string SafeText(string? value)
        {
            if (value == null)
            {
                return "";
            }
            return value;
        }
    }

    /// <summary>
    /// 日志积木注册——进程启动时调用一次
    /// </summary>
    public static class LogBrickRegistration
    {
        /// <summary>
        /// 注册全部日志积木
        /// </summary>
        public static void RegisterAll()
        {
            RegisterWrite();
            RegisterGetAll();
            RegisterCount();
            RegisterClear();
        }

        /// <summary>
        /// 注册 log.write
        /// </summary>
        private static void RegisterWrite()
        {
            BrickContract contract = new BrickContract("log.write", "Mau.Bricks.LogBrick.Write");
            contract.Inputs.Add(new BrickPort("module", typeof(string), "模块名"));
            contract.Inputs.Add(new BrickPort("level", typeof(int), "0=INFO 2=WARN 3=ERROR"));
            contract.Inputs.Add(new BrickPort("message", typeof(string), "消息"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 log.all
        /// </summary>
        private static void RegisterGetAll()
        {
            BrickContract contract = new BrickContract("log.all", "Mau.Bricks.LogBrick.GetAll");
            contract.Outputs.Add(new BrickPort("text", typeof(string), "日志文本行"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 log.count
        /// </summary>
        private static void RegisterCount()
        {
            BrickContract contract = new BrickContract("log.count", "Mau.Bricks.LogBrick.Count");
            contract.Outputs.Add(new BrickPort("count", typeof(int), "日志条数"));
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }

        /// <summary>
        /// 注册 log.clear
        /// </summary>
        private static void RegisterClear()
        {
            BrickContract contract = new BrickContract("log.clear", "Mau.Bricks.LogBrick.Clear");
            contract.Return = BrickReturnKind.Bool;
            contract.Duration = BrickDuration.Sync;
            contract.Thread = "main";
            BrickRegistry.Register(contract);
        }
    }
}
