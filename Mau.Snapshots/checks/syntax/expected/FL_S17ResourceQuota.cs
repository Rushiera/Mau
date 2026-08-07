// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S17ResourceQuota
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S17ResourceQuota 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S17ResourceQuota : IObservableFlow
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
        /// 资源 R_Pool：槽位 4（配额）
        /// </summary>
        private int R_Pool_avail;

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
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_S17ResourceQuota()
        {
            _logs = new FlowLog();
            T_Run_Cube = new Cube(60);
            R_Pool_avail = 4;
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
            // [T_Run] 前置检查
            if (P_Start && R_Pool_avail > 0 && T_Run_Cube.IsIdle())
            {
                // 信号消费
                P_Start = false;
                T_Run_Cube.Start();
                R_Pool_avail = R_Pool_avail - 1;
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_LOG_001.Write(_module, _level, _message);
                if (ok)
                {
                    // 正常后置注册
                    P_Done = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }
                    R_Pool_avail = R_Pool_avail + 1;

                // 同步积木当帧完成
                T_Run_Cube.Complete();
            }

            // [T_Run] 时限检查
            if (T_Run_Cube.IsRunning())
            {
                T_Run_Cube.TickFrame();
                if (T_Run_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    R_Pool_avail = R_Pool_avail + 1;
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
            ResSnapshot[] res = new ResSnapshot[1];
            res[0] = new ResSnapshot("R_Pool", false, 4 - R_Pool_avail, 4);
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
// #MAU_CHECKSUM:SHA256:2AF02A45FBF6C3EE0025E92D3A174A3F4BA2D2013C97328A12E1EA871807592C
