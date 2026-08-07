// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: S19DebugField
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;
using System;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// S19DebugField 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_S19DebugField : IObservableFlow
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
        public FL_S19DebugField()
        {
            _logs = new FlowLog();
            T_Run_Cube = new Cube(60);
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
                P_Start = false;
                T_Run_Cube.Start();
            _logs.Add(new MauDebug(_frame, "T_Run", "Fired", $"转换 {_input} → {_output}"));
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.BRIK_FILE_001.Convert(_input, _output);
                if (ok)
                {
                    // 正常后置注册
                    P_Done = true;
                    _logs.Add(new MauDebug(_frame, "T_Run", "Ok", $"转换 {_input} → {_output}"));
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                    _logs.Add(new MauDebug(_frame, "T_Run", "Error", $"转换 {_input} → {_output}"));
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
                    _logs.Add(new MauDebug(_frame, "T_Run", "Timeout", $"转换 {_input} → {_output}"));
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

    // #BRICK:BRIK-FILE-001 BEGIN
namespace Mau.Bricks
{
    /// <summary>
    /// 文件积木——file.convert 转换文件（独立实现，不走受控根）
    /// </summary>
    public static class BRIK_FILE_001
    {
        /// <summary>
        /// 转换文件——真实实现（读取→转码→写入）
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
        /// <returns>转换是否成功</returns>
        public static bool Convert(string input, string output)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(input);
                // 第一期：转码 = 读取后原样写出——编码转换算法留积木内部后续实现
                System.IO.File.WriteAllBytes(output, bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

    // #BRICK:BRIK-FILE-001 END
// #MAU_CHECKSUM:SHA256:0DAD3C7C3CAEDC119A667EAE9BA7815E6FFC5E17531FDA005D8CCEA9A12C7D79
