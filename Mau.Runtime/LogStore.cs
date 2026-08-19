using System;
using System.Collections.Generic;

namespace Mau.Runtime
{
    /// <summary>
    /// 日志总账（程序级）——统一观测与时序真源（design-ch4-observe §二）。
    /// 状态经 DataBox scope 存储（BRIK 唯一数据协议）；磁盘投影到 Data/runs/&lt;ts&gt;/ 三文件（log.all/oa.all/err.all）。
    /// 审计事件经 Type 字段并入（audit.* 前缀——O2）；危险等级 Level 1=INFO/2=WARN/3=ERR。
    /// </summary>
    public static class LogStore
    {
        /// <summary>
        /// 日志条目类型——统一观测载体（V2：Type/Payload 字段 + Level 语义 1/2/3）
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
            /// 危险等级——1=INFO / 2=WARN / 3=ERR（旧 0 归一 1）
            /// </summary>
            public int Level;

            /// <summary>
            /// 消息
            /// </summary>
            public string Message;

            /// <summary>
            /// 类别——CHAT/TOOL/CMD/OA/LLM/AUDIT/SYS/"" 等（重复信息面：命令总线/工单系统/工具执行/会话过程）
            /// </summary>
            public string Category;

            /// <summary>
            /// 帧号——写入时全局帧号（FlowRunner 驱动；-1=无帧号来源）
            /// </summary>
            public long Frame;

            /// <summary>
            /// 审计结构化事件名——audit.{source}.{event}（cmd.set / oa.post / flow.register / app.start...）；非审计为 ""
            /// </summary>
            public string Type;

            /// <summary>
            /// 结构化载荷——审计 props 序列化 JSON；非审计为 ""
            /// </summary>
            public string Payload;
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
        /// 磁盘持久化路径——空=仅内存（V2：log.all 主文件路径）
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
        /// 运行目录——Data/runs/&lt;ts&gt;（三文件写者根；空=未配置）
        /// </summary>
        private static string _runDir = "";

        /// <summary>
        /// 主写者——log.all（全量时序）
        /// </summary>
        private static System.IO.StreamWriter? _logWriter;

        /// <summary>
        /// OA 投影写者——oa.all（category=OA 专属：Dog 生命周期帧序链）
        /// </summary>
        private static System.IO.StreamWriter? _oaWriter;

        /// <summary>
        /// ERR 投影写者——err.all（Level=3 错误专属——排查第一入口）
        /// </summary>
        private static System.IO.StreamWriter? _errWriter;

        /// <summary>
        /// 写盘锁——并发 Add 串行化
        /// </summary>
        private static readonly object _writeGate = new object();

        /// <summary>
        /// 上次 flush 时间——1s 定时刷
        /// </summary>
        private static DateTime _lastFlush = DateTime.MinValue;

        /// <summary>
        /// Console 订阅——非空时每条 Add 打印一行（窗口与 log.all 同构——O4 全量接入）
        /// </summary>
        public static Action<string>? ConsoleSink;

        /// <summary>
        /// 配置磁盘日志文件——单文件兼容模式（V1 行为；O4 后宿主切 ConfigureRuns——三文件）
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
        /// 配置运行目录——三文件写者（design-ch4-observe §三）：log.all（全量）+ oa.all（OA 投影）+ err.all（L3 投影）。
        /// 后续调用幂等——重复配置重开写者（路径变化）。
        /// </summary>
        /// <param name="runDir">Data/runs/&lt;yyyyMMdd_HHmmss&gt;</param>
        public static void ConfigureRuns(string runDir)
        {
            if (runDir == null)
            {
                runDir = "";
            }
            if (runDir.Length == 0)
            {
                return;
            }
            lock (_writeGate)
            {
                CloseWriters();
                System.IO.Directory.CreateDirectory(runDir);
                _runDir = runDir;
                LogFilePath = System.IO.Path.Combine(runDir, "log.all");
                _logWriter = OpenWriter(System.IO.Path.Combine(runDir, "log.all"));
                _oaWriter = OpenWriter(System.IO.Path.Combine(runDir, "oa.all"));
                _errWriter = OpenWriter(System.IO.Path.Combine(runDir, "err.all"));
            }
        }

        /// <summary>
        /// 清空内存总账——测试隔离专用（AuditSerial 集合测试开头调用；运行时不使用）
        /// </summary>
        public static void ClearForTest()
        {
            lock (Sync)
            {
                AllLog.Clear();
            }
        }

        /// <summary>
        /// 写入一条结构化日志（V1 兼容签名——Type/Payload 空；level 0/1 归一 INFO）
        /// </summary>
        /// <param name="module">模块名</param>
        /// <param name="level">危险等级——0/1=INFO 2=WARN 3=ERR</param>
        /// <param name="message">消息</param>
        /// <param name="category">类别——""/CMD/OA/CHAT/TOOL/LLM/AUDIT</param>
        public static void Add(string module, int level, string message, string category)
        {
            Add(module, level, message, category, "", "", 0, -1, false);
        }

        /// <summary>
        /// 写入一条结构化日志（V2 七参——Type/Payload/maxLen；默认帧与落盘）
        /// </summary>
        public static void Add(string module, int level, string message, string category, string type, string payload, int maxLen)
        {
            Add(module, level, message, category, type, payload, maxLen, -1, false);
        }

        /// <summary>
        /// 写入一条结构化日志（V2 全参数——统一观测唯一入口）。
        /// 内存总账 + 磁盘三投影（log.all 全量 / oa.all 投影 / err.all 投影）+ Console 订阅打印。
        /// </summary>
        /// <param name="module">模块名</param>
        /// <param name="level">危险等级——0/1=INFO 2=WARN 3=ERR（0 归一 1）</param>
        /// <param name="message">消息（maxLen&gt;0 时超长截断）</param>
        /// <param name="category">类别——CHAT/TOOL/CMD/OA/LLM/AUDIT/SYS/""</param>
        /// <param name="type">审计事件名 audit.*（非审计空串）</param>
        /// <param name="payload">结构化载荷 JSON（非审计空串）</param>
        /// <param name="maxLen">消息截断上限字符——0=不截断；工具结果入 Log 建议 100（设计拍板）</param>
        /// <param name="frame">显式帧号（-1=FlowRunner.GlobalFrame——审计 Record 带调用侧 frame）</param>
        /// <param name="skipDisk">true=仅内存/SSE/Console（不落盘投影——L0-TRACE 类）</param>
        public static void Add(string module, int level, string message, string category, string type, string payload, int maxLen, long frame, bool skipDisk)
        {
            if (level <= 0)
            {
                level = 1;
            }
            if (maxLen > 0 && message != null && message.Length > maxLen)
            {
                message = message.Substring(0, maxLen) + "…[截断:" + message.Length.ToString() + "]";
            }
            if (message == null)
            {
                message = "";
            }
            if (category == null)
            {
                category = "";
            }
            if (type == null)
            {
                type = "";
            }
            if (payload == null)
            {
                payload = "";
            }
            LogEntry entry;
            entry.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.Module = SafeText(module);
            entry.Level = level;
            entry.Message = message;
            entry.Category = category;
            if (frame >= 0)
            {
                entry.Frame = frame;
            }
            else
            {
                entry.Frame = FlowRunner.GlobalFrame;
            }
            entry.Type = type;
            entry.Payload = payload;
            lock (Sync)
            {
                AllLog.Add(entry);
            }
            // [段1] 磁盘三投影——同锁同批（log.all 全量 + oa.all/err.all 投影）；skipDisk 类不落盘（L0-TRACE）
            if (!skipDisk && (LogFilePath.Length > 0 || _runDir.Length > 0))
            {
                try
                {
                    lock (_writeGate)
                    {
                        EnsureWriters();
                        string line = FormatLine(entry);
                        if (_logWriter != null)
                        {
                            _logWriter.WriteLine(line);
                        }
                        if (_oaWriter != null && entry.Category == "OA")
                        {
                            _oaWriter.WriteLine(line);
                        }
                        if (_errWriter != null && entry.Level >= 3)
                        {
                            _errWriter.WriteLine(line);
                        }
                        FlushIfDue();
                    }
                }
                catch
                {
                    // 磁盘写失败——内存总账已保留
                }
            }
            // [段2] Console 订阅——窗口同构（O4 全量接入后宿主过程行不再直打）；skipDisk 类（L0-TRACE）跳过感官通道
            if (!skipDisk && ConsoleSink != null)
            {
                try
                {
                    ConsoleSink(FormatLine(entry));
                }
                catch
                {
                    // 订阅异常不影响日志主链
                }
            }
        }

        /// <summary>
        /// 行格式——F{frame} | L{level} | INFO/WARN/ERR | [{category}] | {message} [payload]（log.all 与窗口同构）
        /// </summary>
        /// <param name="entry">条目</param>
        /// <returns>格式行</returns>
        private static string FormatLine(LogEntry entry)
        {
            string line = entry.Time + " | F" + entry.Frame.ToString() + " | " + LevelText(entry.Level);
            if (entry.Category.Length > 0)
            {
                line = line + " | [" + entry.Category + "]";
            }
            line = line + " | " + entry.Message;
            if (entry.Type.Length > 0)
            {
                line = line + " | #" + entry.Type;
            }
            if (entry.Payload.Length > 0)
            {
                line = line + " | " + entry.Payload;
            }
            return line;
        }

        /// <summary>
        /// 级别文本——0/1=INFO 2=WARN >=3=ERR
        /// </summary>
        /// <param name="level">级别数字</param>
        /// <returns>文本</returns>
        public static string LevelText(int level)
        {
            if (level >= 3)
            {
                return "ERR";
            }
            if (level == 2)
            {
                return "WARN";
            }
            return "INFO";
        }

        /// <summary>
        /// 打开写者——append + 读共享（不锁文件）
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <returns>写者</returns>
        private static System.IO.StreamWriter OpenWriter(string path)
        {
            System.IO.StreamWriter w = new System.IO.StreamWriter(new System.IO.FileStream(path, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite), new System.Text.UTF8Encoding(false));
            // AutoFlush——窗口/长进程尾部滞留修复（1s 批量依赖后续写入触发；无写入则尾部滞留——强杀丢尾判例 2026-08-19）
            w.AutoFlush = true;
            return w;
        }

        /// <summary>
        /// 确保写者存在——单文件兼容模式（ConfigureLogFile 调用过但未 ConfigureRuns）
        /// </summary>
        private static void EnsureWriters()
        {
            if (_logWriter == null && LogFilePath.Length > 0)
            {
                string? dir = System.IO.Path.GetDirectoryName(LogFilePath);
                if (dir != null && dir.Length > 0)
                {
                    System.IO.Directory.CreateDirectory(dir);
                }
                _logWriter = OpenWriter(LogFilePath);
            }
        }

        /// <summary>
        /// 写者就绪且超过 1s 未 flush——批量落盘
        /// </summary>
        private static void FlushIfDue()
        {
            DateTime now = DateTime.Now;
            if ((now - _lastFlush).TotalSeconds >= 1.0)
            {
                if (_logWriter != null)
                {
                    _logWriter.Flush();
                }
                if (_oaWriter != null)
                {
                    _oaWriter.Flush();
                }
                if (_errWriter != null)
                {
                    _errWriter.Flush();
                }
                _lastFlush = now;
            }
        }

        /// <summary>
        /// 关闭全部写者——宿主退出前调用（幂等）
        /// </summary>
        public static void CloseWriters()
        {
            if (_logWriter != null)
            {
                try
                {
                    _logWriter.Flush();
                }
                catch
                {
                }
                _logWriter.Dispose();
                _logWriter = null;
            }
            if (_oaWriter != null)
            {
                try
                {
                    _oaWriter.Flush();
                }
                catch
                {
                }
                _oaWriter.Dispose();
                _oaWriter = null;
            }
            if (_errWriter != null)
            {
                try
                {
                    _errWriter.Flush();
                }
                catch
                {
                }
                _errWriter.Dispose();
                _errWriter = null;
            }
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