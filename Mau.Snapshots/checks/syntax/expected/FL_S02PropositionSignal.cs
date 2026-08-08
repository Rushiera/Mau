// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S02PropositionSignal
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;
using System;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S02PropositionSignal 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S02PropositionSignal : IObservableFlow
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
        /// 构造：初始化日志缓冲
        /// </summary>
        public FL_S02PropositionSignal()
        {
            _logs = new FlowLog();
        }

        /// <summary>
        /// 外部投递信号：Start
        /// </summary>
        /// <param name="input">参数 input</param>
        /// <param name="output">参数 output</param>
        public void FireStart(string input, string output)
        {
            _input = input;
            _output = output;
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
            if (P_Start)
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_Run", "Consume", "P_Start")); }
                P_Start = false;
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
            trans[0] = new TransSnapshot("T_Run", "Idle", 0, 0);
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
// #MAU_CHECKSUM:SHA256:6E3DAD4E4C995DAD97EA70B17E80A920D44C2F372DB1926B66054CC872F5EA70
