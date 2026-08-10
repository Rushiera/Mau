// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S08Disjunction
// 基座: Mau.Runtime/v0.1

// Fire 契约:
//   FireA(string input, string output)
//   FireB(string input, string output)

using Mau.Runtime;
using System.Threading.Tasks;
using System;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S08Disjunction 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S08Disjunction : IObservableFlow
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
        /// 命题 P_A：信号，消费即清除
        /// </summary>
        private bool P_A;

        /// <summary>
        /// 命题 P_B：信号，消费即清除
        /// </summary>
        private bool P_B;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_Run 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_Run 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_Run 的时限 Cube（60 帧有限模式）
        /// </summary>
        private Cube T_Run_Cube;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_S08Disjunction()
        {
            _logs = new FlowLog();
            T_Run_Cube = new Cube(60);
        }

        /// <summary>
        /// 外部投递信号：A
        /// </summary>
        /// <param name="input">参数 input</param>
        /// <param name="output">参数 output</param>
        public void FireA(string input, string output)
        {
            _input = input;
            _output = output;
            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "Fire", "Signal", "P_A")); TraceAudit("fire", "P_A", "fire"); }
            P_A = true;
        }

        /// <summary>
        /// 外部投递信号：B
        /// </summary>
        /// <param name="input">参数 input</param>
        /// <param name="output">参数 output</param>
        public void FireB(string input, string output)
        {
            _input = input;
            _output = output;
            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "Fire", "Signal", "P_B")); TraceAudit("fire", "P_B", "fire"); }
            P_B = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Run] 前置检查
            if ((P_A || P_B) && T_Run_Cube.IsIdle())
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_Run", "Consume", "P_A")); TraceAudit("consume", "P_A", "consume"); }
                P_A = false;
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_Run", "Consume", "P_B")); TraceAudit("consume", "P_B", "consume"); }
                P_B = false;
                T_Run_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_FILE_001.Convert(_input, _output);
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
            PropSnapshot[] props = new PropSnapshot[4];
            props[0] = new PropSnapshot("P_A", "Signal", P_A);
            props[1] = new PropSnapshot("P_B", "Signal", P_B);
            props[2] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[3] = new PropSnapshot("P_Failed", "Fact", P_Failed);
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
        /// trace 审计写入——数据流追踪事件可选写审计（A.3：AuditStore.Default 存在时记录，L3 级不落盘）
        /// </summary>
        /// <param name="kind">trace 类别（fire/consume/set）</param>
        /// <param name="name">命题或变迁名</param>
        /// <param name="result">结果文本</param>
        private void TraceAudit(string kind, string name, string result)
        {
            if (AuditStore.Default != null)
            {
                AuditStore.Default.Record("Flow", "trace." + kind, -1, new AuditProp[] {
                    new AuditProp("flow", this.GetType().Name),
                    new AuditProp("name", name),
                    new AuditProp("result", result),
                    new AuditProp("flow_frame", _frame.ToString())
                }, false);
            }
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
// #MAU_CHECKSUM:SHA256:BCF5895DF99C925636D824989BC27F9097F8567C7DFA5F58FB35CCFD6573269A
