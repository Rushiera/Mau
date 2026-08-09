// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S12ExclusivePost
// 基座: Mau.Runtime/v0.1

// Fire 契约:
//   FireStart(string module, int level, string message)

using Mau.Runtime;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S12ExclusivePost 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S12ExclusivePost : IObservableFlow
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
        /// 数据流追踪开关——SetTraceDataFlow 控制（D2 调试基建：输出赋值/信号投递消费记录）
        /// </summary>
        private bool _traceDataFlow;

        /// <summary>
        /// 命题 P_Start：信号，消费即清除
        /// </summary>
        private bool P_Start;

        /// <summary>
        /// 命题 P_Ok：终态事实，置位后保持
        /// </summary>
        private bool P_Ok;

        /// <summary>
        /// 命题 P_Err：终态事实，置位后保持
        /// </summary>
        private bool P_Err;

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
        public FL_S12ExclusivePost()
        {
            _logs = new FlowLog();
            T_Run_Cube = new Cube(60);
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
            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "Fire", "Signal", "P_Start")); }
            P_Start = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Run] 前置检查
            if (P_Start && T_Run_Cube.IsIdle())
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_Run", "Consume", "P_Start")); }
                P_Start = false;
                T_Run_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_LOG_001.Write(_module, _level, _message);
                if (ok)
                {
                    // 正常后置注册
                    P_Ok = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Err = true;
                }

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
                    P_Err = true;
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
            props[1] = new PropSnapshot("P_Ok", "Fact", P_Ok);
            props[2] = new PropSnapshot("P_Err", "Fact", P_Err);
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
        /// 开启数据流追踪——输出端口赋值/信号投递消费记录 MauDebug（D2 调试基建）
        /// </summary>
        /// <param name="enabled">true=记录数据流日志</param>
        public void SetTraceDataFlow(bool enabled)
        {
            _traceDataFlow = enabled;
        }

        /// <summary>
        /// 查询结果：Ok
        /// </summary>
        /// <returns>Ok成立</returns>
        public bool IsOk()
        {
            return P_Ok;
        }

        /// <summary>
        /// 重置结果：Ok
        /// </summary>
        public void ResetOk()
        {
            P_Ok = false;
        }

        /// <summary>
        /// 查询结果：Err
        /// </summary>
        /// <returns>Err成立</returns>
        public bool IsErr()
        {
            return P_Err;
        }

        /// <summary>
        /// 重置结果：Err
        /// </summary>
        public void ResetErr()
        {
            P_Err = false;
        }

    }
}
// #MAU_CHECKSUM:SHA256:E2BCA6F18BA616A08FED7E1D57FE5C9B456D83D1A0A3FB508E55E9BD5FAB75CB
