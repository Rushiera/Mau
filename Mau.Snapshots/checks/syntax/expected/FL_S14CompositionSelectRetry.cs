// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S14CompositionSelectRetry
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
    /// S14CompositionSelectRetry 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S14CompositionSelectRetry : IObservableFlow
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
        /// 命题 P_A：终态事实，置位后保持
        /// </summary>
        private bool P_A;

        /// <summary>
        /// 命题 P_B：终态事实，置位后保持
        /// </summary>
        private bool P_B;

        /// <summary>
        /// 命题 P_C：终态事实，置位后保持
        /// </summary>
        private bool P_C;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_A 的动作参数——module
        /// </summary>
        private string _module = null!;

        /// <summary>
        /// 变迁 T_A 的动作参数——level
        /// </summary>
        private int _level;

        /// <summary>
        /// 变迁 T_A 的动作参数——message
        /// </summary>
        private string _message = null!;

        /// <summary>
        /// 变迁 T_A 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_A_Cube;

        /// <summary>
        /// 变迁 T_B 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_B_Cube;

        /// <summary>
        /// 变迁 T_C 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_C_Cube;

        /// <summary>
        /// 变迁 T_Finish 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_Finish_Cube;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_S14CompositionSelectRetry()
        {
            _logs = new FlowLog();
            T_A_Cube = new Cube(60);
            T_B_Cube = new Cube(60);
            T_C_Cube = new Cube(60);
            T_Finish_Cube = new Cube(60);
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
            // [T_A] 前置检查
            if (P_Start && T_A_Cube.IsIdle())
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_A", "Consume", "P_Start")); }
                P_Start = false;
                T_A_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_LOG_001.Write(_module, _level, _message);
                if (ok)
                {
                    // 正常后置注册
                    P_A = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_A_Cube.Complete();
            }

            // [T_A] 时限检查
            if (T_A_Cube.IsRunning())
            {
                T_A_Cube.TickFrame();
                if (T_A_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_A_Cube.Complete();
                }
            }

            // [T_B] 前置检查
            if (P_A && T_B_Cube.IsIdle())
            {
                T_B_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_LOG_001.Write(_module, _level, _message);
                if (ok)
                {
                    // 正常后置注册
                    P_B = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_B_Cube.Complete();
            }

            // [T_B] 时限检查
            if (T_B_Cube.IsRunning())
            {
                T_B_Cube.TickFrame();
                if (T_B_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_B_Cube.Complete();
                }
            }

            // [T_C] 前置检查
            if (P_A && T_C_Cube.IsIdle())
            {
                T_C_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_LOG_001.Write(_module, _level, _message);
                if (ok)
                {
                    // 正常后置注册
                    P_C = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_C_Cube.Complete();
            }

            // [T_C] 时限检查
            if (T_C_Cube.IsRunning())
            {
                T_C_Cube.TickFrame();
                if (T_C_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_C_Cube.Complete();
                }
            }

            // [T_Finish] 前置检查
            if ((P_B || P_C) && T_Finish_Cube.IsIdle())
            {
                T_Finish_Cube.Start();
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

                // 同步积木当帧完成
                T_Finish_Cube.Complete();
            }

            // [T_Finish] 时限检查
            if (T_Finish_Cube.IsRunning())
            {
                T_Finish_Cube.TickFrame();
                if (T_Finish_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Finish_Cube.Complete();
                }
            }
        }

        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[6];
            props[0] = new PropSnapshot("P_Start", "Signal", P_Start);
            props[1] = new PropSnapshot("P_A", "Fact", P_A);
            props[2] = new PropSnapshot("P_B", "Fact", P_B);
            props[3] = new PropSnapshot("P_C", "Fact", P_C);
            props[4] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[5] = new PropSnapshot("P_Failed", "Fact", P_Failed);
            TransSnapshot[] trans = new TransSnapshot[4];
            trans[0] = new TransSnapshot("T_A", T_A_Cube.State.ToString(), T_A_Cube.ElapsedFrames, T_A_Cube.LimitFrames);
            trans[1] = new TransSnapshot("T_B", T_B_Cube.State.ToString(), T_B_Cube.ElapsedFrames, T_B_Cube.LimitFrames);
            trans[2] = new TransSnapshot("T_C", T_C_Cube.State.ToString(), T_C_Cube.ElapsedFrames, T_C_Cube.LimitFrames);
            trans[3] = new TransSnapshot("T_Finish", T_Finish_Cube.State.ToString(), T_Finish_Cube.ElapsedFrames, T_Finish_Cube.LimitFrames);
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

        // 组合 FL_Sel:  [experimental — documentation-only，执行语义未实现]
        //   序列: T_A
        //   选择: T_A → T_B | T_C
        //   重试: 3

        /// <summary>
        /// 查询结果：A
        /// </summary>
        /// <returns>A成立</returns>
        public bool IsA()
        {
            return P_A;
        }

        /// <summary>
        /// 重置结果：A
        /// </summary>
        public void ResetA()
        {
            P_A = false;
        }

        /// <summary>
        /// 查询结果：B
        /// </summary>
        /// <returns>B成立</returns>
        public bool IsB()
        {
            return P_B;
        }

        /// <summary>
        /// 重置结果：B
        /// </summary>
        public void ResetB()
        {
            P_B = false;
        }

        /// <summary>
        /// 查询结果：C
        /// </summary>
        /// <returns>C成立</returns>
        public bool IsC()
        {
            return P_C;
        }

        /// <summary>
        /// 重置结果：C
        /// </summary>
        public void ResetC()
        {
            P_C = false;
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
// #MAU_CHECKSUM:SHA256:95BA7C7A7B4C687E2849E257C0F23AF72DDDACF47BFB52EB4FF35D4DAB14456C
