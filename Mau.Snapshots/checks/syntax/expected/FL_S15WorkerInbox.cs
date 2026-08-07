// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S15WorkerInbox
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S15WorkerInbox 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S15WorkerInbox : IObservableFlow
    {
        /// <summary>
        /// 内部帧号——每 Tick 自增
        /// </summary>
        private long _frame;

        /// <summary>
        /// 环形调试日志——200 条上限
        /// </summary>
        private FlowLog _logs;

        /// <summary>
        /// 命题 P_Start：信号，消费即清除
        /// </summary>
        private bool P_Start;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_Run 的动作参数——module
        /// </summary>
        private string _module = null!;

        /// <summary>
        /// 变迁 T_Run 的动作参数——level
        /// </summary>
        private int _level;

        /// <summary>
        /// 变迁 T_Run 的动作参数——message
        /// </summary>
        private string _message = null!;

        /// <summary>
        /// 变迁 T_Run 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_Run_Cube;

        /// <summary>
        /// 变迁 T_Run 的 Inbox 双缓冲——后台结果回投
        /// </summary>
        private Inbox<bool> T_Run_Inbox;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_S15WorkerInbox()
        {
            _logs = new FlowLog();
            T_Run_Cube = new Cube(60);
            T_Run_Inbox = new Inbox<bool>();
        }

        /// <summary>
        /// 外部投递信号：Start
        /// </summary>
        /// <param name="module">参数 module</param>
        /// <param name="level">参数 level</param>
        /// <param name="message">参数 message</param>
        public void FireStart(string module, int level, string message)
        {
            _module = module;
            _level = level;
            _message = message;
            P_Start = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Run] worker+inbox 前置检查
            if (P_Start && T_Run_Cube.IsIdle())
            {
                // 信号消费
                P_Start = false;
                T_Run_Cube.Start();
                // 启动后台任务——积木在 worker 线程执行，结果回投 inbox
                var inbox = T_Run_Inbox;
                string f_module = _module;
                int f_level = _level;
                string f_message = _message;
                Task.Run(() =>
                {
                    try
                    {
                        bool ok = Mau.Bricks.BRIK_LOG_001.Write(f_module, f_level, f_message);
                        inbox.Enqueue(ok);
                    }
                    catch
                    {
                        // 后台异常——回投失败，走错误后置
                        inbox.Enqueue(false);
                    }
                });
            }

            // [T_Run] inbox 排空
            T_Run_Inbox.Drain(ok =>
            {
                if (!T_Run_Cube.IsRunning())
                {
                    return;
                }
                if (ok)
                {
                    P_Done = true;
                }
                else
                {
                    P_Failed = true;
                }
                T_Run_Cube.Complete();
            });

            // [T_Run] 时限检查
            if (T_Run_Cube.IsRunning())
            {
                T_Run_Cube.TickFrame();
                if (T_Run_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Run_Cube.Complete();
                }
            }
        }

        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[3];
            props[0] = new PropSnapshot("P_Start", "Signal", P_Start);
            props[1] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[2] = new PropSnapshot("P_Failed", "Fact", P_Failed);
            TransSnapshot[] trans = new TransSnapshot[1];
            trans[0] = new TransSnapshot("T_Run", T_Run_Cube.State.ToString(), T_Run_Cube.ElapsedFrames, T_Run_Cube.LimitFrames);
            ResSnapshot[] res = new ResSnapshot[0];
            return new RuntimeStatus(_frame, props, trans, res);
        }

        /// <summary>
        /// 获取全量调试日志
        /// </summary>
        /// <returns>日志数组，时间顺序</returns>
        public MauDebug[] GetLogs()
        {
            return _logs.GetAll();
        }

        /// <summary>
        /// 查询结果：Done
        /// </summary>
        /// <returns>Done成立</returns>
        public bool IsDone()
        {
            return P_Done;
        }

        /// <summary>
        /// 重置结果：Done
        /// </summary>
        public void ResetDone()
        {
            P_Done = false;
        }

        /// <summary>
        /// 查询结果：Failed
        /// </summary>
        /// <returns>Failed成立</returns>
        public bool IsFailed()
        {
            return P_Failed;
        }

        /// <summary>
        /// 重置结果：Failed
        /// </summary>
        public void ResetFailed()
        {
            P_Failed = false;
        }

    }
}

    // #BRICK:BRIK-LOG-001 BEGIN
namespace Mau.Bricks
{
    /// <summary>
    /// 日志积木——log.write 写入一条结构化日志（依赖 LogStore）
    /// </summary>
    public static class BRIK_LOG_001
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

    // #BRICK:BRIK-LOG-001 END
// #MAU_CHECKSUM:SHA256:76C644E6DB9CAA46BAB9806D233CDB95E9882DB7D817D286527F32B6FD100B7F
