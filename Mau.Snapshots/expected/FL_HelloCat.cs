// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: HelloCat
// 基座: Mau.Runtime/v0.1

// Fire 契约:
//   FireAsk(string module, int level, string message)

using Mau.Runtime;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Text;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// HelloCat 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_HelloCat : IObservableFlow, CH4.Contracts.ICat
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
        /// 命题 P_Ask：信号，消费即清除
        /// </summary>
        private bool P_Ask;

        /// <summary>
        /// 命题 P_Done：终态事实，置位后保持
        /// </summary>
        private bool P_Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_Hello 的动作参数——module
        /// </summary>
        private string _module = null!;

        /// <summary>
        /// 变迁 T_Hello 的动作参数——level
        /// </summary>
        private int _level;

        /// <summary>
        /// 变迁 T_Hello 的动作参数——message
        /// </summary>
        private string _message = null!;

        /// <summary>
        /// 构造：初始化日志缓冲
        /// </summary>
        public FL_HelloCat()
        {
            _logs = new FlowLog();
        }

        /// <summary>
        /// 外部投递信号：Ask
        /// </summary>
        /// <param name="module">参数 module</param>
        /// <param name="level">参数 level</param>
        /// <param name="message">参数 message</param>
        public void FireAsk(string module, int level, string message)
        {
            _module = module;
            _level = level;
            _message = message;
            if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "Fire", "Signal", "P_Ask")); TraceAudit("fire", "P_Ask", "fire"); }
            P_Ask = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Hello] 前置检查
            if (P_Ask)
            {
                // 信号消费
                if (_traceDataFlow) { _logs.Add(new MauDebug(_frame, "T_Hello", "Consume", "P_Ask")); TraceAudit("consume", "P_Ask", "consume"); }
                P_Ask = false;
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
            }
        }

        /// <summary>
        /// 获取运行时状态快照——全量截面
        /// </summary>
        /// <returns>当前帧状态</returns>
        public RuntimeStatus GetStatus()
        {
            PropSnapshot[] props = new PropSnapshot[3];
            props[0] = new PropSnapshot("P_Ask", "Signal", P_Ask);
            props[1] = new PropSnapshot("P_Done", "Fact", P_Done);
            props[2] = new PropSnapshot("P_Failed", "Fact", P_Failed);
            TransSnapshot[] trans = new TransSnapshot[1];
            trans[0] = new TransSnapshot("T_Hello", "Idle", 0, 0);
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
// #MAU_CHECKSUM:SHA256:E5BE52E5B3A92210519155D7F8129FA59CD57475E3661BD7B0EA4393EC12756F