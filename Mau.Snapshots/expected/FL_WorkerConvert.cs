// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: WorkerConvert
// 基座: Mau.Runtime/v0.1

// Fire 契约:
//   FireInput(string input, string output)

using Mau.Runtime;
using System.Threading.Tasks;
using System;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// WorkerConvert 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_WorkerConvert : IObservableFlow
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
        /// 命题 P_Input：信号，消费即清除
        /// </summary>
        private bool P_Input;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_WorkerConvert 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_WorkerConvert 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_WorkerConvert 的时限 Cube（300 帧有限模式）
        /// </summary>
        private Cube T_WorkerConvert_Cube;

        /// <summary>
        /// 变迁 T_WorkerConvert 的 Inbox 双缓冲——后台结果回投
        /// </summary>
        private Inbox<bool> T_WorkerConvert_Inbox;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_WorkerConvert()
        {
            _logs = new FlowLog();
            T_WorkerConvert_Cube = new Cube(300);
            T_WorkerConvert_Inbox = new Inbox<bool>();
        }

        /// <summary>
        /// 外部投递信号：Input
        /// </summary>
        /// <param name="input">参数 input</param>
        /// <param name="output">参数 output</param>
        public void FireInput(string input, string output)
        {
            _input = input;
            _output = output;
            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "Fire", "Signal", "P_Input")); TraceAudit("fire", "P_Input", "fire"); }
            P_Input = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_WorkerConvert] worker+inbox 前置检查
            if (P_Input && T_WorkerConvert_Cube.IsIdle())
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_WorkerConvert", "Consume", "P_Input")); TraceAudit("consume", "P_Input", "consume"); }
                P_Input = false;
                T_WorkerConvert_Cube.Start();
                // 启动后台任务——积木在 worker 线程执行，结果回投 inbox
                var inbox = T_WorkerConvert_Inbox;
                string f_input = _input;
                string f_output = _output;
                Task.Run(() =>
                {
                    try
                    {
                        bool ok = Mau.Bricks.BRIK_FILE_001.Convert(f_input, f_output);
                        inbox.Enqueue(ok);
                    }
                    catch
                    {
                        // 后台异常——回投失败，走错误后置
                        inbox.Enqueue(false);
                    }
                });
            }

            // [T_WorkerConvert] inbox 排空
            T_WorkerConvert_Inbox.Drain(ok =>
            {
                if (!T_WorkerConvert_Cube.IsRunning())
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
                T_WorkerConvert_Cube.Complete();
            });

            // [T_WorkerConvert] 时限检查
            if (T_WorkerConvert_Cube.IsRunning())
            {
                T_WorkerConvert_Cube.TickFrame();
                if (T_WorkerConvert_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_WorkerConvert_Cube.Complete();
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
            props[0] = new PropSnapshot("P_Input", "Signal", P_Input);
            props[1] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[2] = new PropSnapshot("P_Failed", "Fact", P_Failed);
            TransSnapshot[] trans = new TransSnapshot[1];
            trans[0] = new TransSnapshot("T_WorkerConvert", T_WorkerConvert_Cube.State.ToString(), T_WorkerConvert_Cube.ElapsedFrames, T_WorkerConvert_Cube.LimitFrames);
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
// #MAU_CHECKSUM:SHA256:8860296A2A49FCD5A6F2DEC5FAE0A7CF79C64FC213BA69E8B8028881ACE96C62