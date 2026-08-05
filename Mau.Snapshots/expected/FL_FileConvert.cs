// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: FileConvert
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// FileConvert 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_FileConvert : IObservableFlow
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
        /// 变迁 T_Convert 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_Convert 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_Convert 的时限 Cube（300 帧有限模式）
        /// </summary>
        private Cube T_Convert_Cube;

        /// <summary>
        /// 构造：初始化日志缓冲与 Cube/Inbox
        /// </summary>
        public FL_FileConvert()
        {
            _logs = new FlowLog();
            T_Convert_Cube = new Cube(300);
        }

        /// <summary>
        /// 外部投递信号：Input
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
        public void FireInput(string input, string output)
        {
            _input = input;
            _output = output;
            P_Input = true;
        }

        /// <summary>
        /// 每帧驱动——由主 Tick 调用
        /// </summary>
        public void Tick()
        {
            _frame = _frame + 1;
            // [T_Convert] 前置检查
            if (P_Input && T_Convert_Cube.IsIdle())
            {
                // 信号消费
                P_Input = false;
                T_Convert_Cube.Start();
            _logs.Add(new MauDebug(_frame, "T_Convert", "Fired", $"开始转换 {_input} → {_output}"));
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
                if (ok)
                {
                    // 正常后置注册
                    P_Done = true;
                    _logs.Add(new MauDebug(_frame, "T_Convert", "Ok", $"开始转换 {_input} → {_output}"));
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                    _logs.Add(new MauDebug(_frame, "T_Convert", "Error", $"开始转换 {_input} → {_output}"));
                }

                // 同步积木当帧完成
                T_Convert_Cube.Complete();
            }

            // [T_Convert] 时限检查
            if (T_Convert_Cube.IsRunning())
            {
                T_Convert_Cube.TickFrame();
                if (T_Convert_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    _logs.Add(new MauDebug(_frame, "T_Convert", "Timeout", $"开始转换 {_input} → {_output}"));
                    T_Convert_Cube.Complete();
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
            trans[0] = new TransSnapshot("T_Convert", T_Convert_Cube.State.ToString(), T_Convert_Cube.ElapsedFrames, T_Convert_Cube.LimitFrames);
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
// #MAU_CHECKSUM:SHA256:C1DC8835082C3CDB6FD63682DA779F07EF2C38E204886EEA564770C331B16FA2