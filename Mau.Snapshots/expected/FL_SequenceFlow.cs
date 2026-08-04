// 本文件由 Mau Translator v0.1 自动生成 —— 请勿手改
// 流程: SequenceFlow
// 基座: Mau.Runtime/v0.1

using Mau.Runtime;
using System.Threading.Tasks;

namespace Mau.Generated.Flows
{
    /// <summary>
    /// SequenceFlow 流程——由 Mau 声明生成
    /// </summary>
    public sealed class FL_SequenceFlow
    {
        /// <summary>
        /// 命题 P_Start：信号，消费即清除
        /// </summary>
        private bool P_Start;

        /// <summary>
        /// 命题 P_Step1Done：终态事实，置位后保持
        /// </summary>
        private bool P_Step1Done;

        /// <summary>
        /// 命题 P_Step2Done：终态事实，置位后保持
        /// </summary>
        private bool P_Step2Done;

        /// <summary>
        /// 命题 P_Step3Done：终态事实，置位后保持
        /// </summary>
        private bool P_Step3Done;

        /// <summary>
        /// 命题 P_Failed：终态事实，置位后保持
        /// </summary>
        private bool P_Failed;

        /// <summary>
        /// 变迁 T_Step1 的动作参数——input
        /// </summary>
        private string _input = null!;

        /// <summary>
        /// 变迁 T_Step1 的动作参数——output
        /// </summary>
        private string _output = null!;

        /// <summary>
        /// 变迁 T_Step1 的时限 Cube（100 帧有限模式）
        /// </summary>
        private Cube T_Step1_Cube;

        /// <summary>
        /// 变迁 T_Step2 的时限 Cube（100 帧有限模式）
        /// </summary>
        private Cube T_Step2_Cube;

        /// <summary>
        /// 变迁 T_Step3 的时限 Cube（100 帧有限模式）
        /// </summary>
        private Cube T_Step3_Cube;

        /// <summary>
        /// 构造：初始化 Cube 与 Inbox
        /// </summary>
        public FL_SequenceFlow()
        {
            T_Step1_Cube = new Cube(100);
            T_Step2_Cube = new Cube(100);
            T_Step3_Cube = new Cube(100);
        }

        /// <summary>
        /// 外部投递信号：Start
        /// </summary>
        /// <param name="input">输入文件路径</param>
        /// <param name="output">输出文件路径</param>
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
            // [T_Step1] 前置检查
            if (P_Start && T_Step1_Cube.IsIdle())
            {
                // 信号消费
                P_Start = false;
                T_Step1_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
                if (ok)
                {
                    // 正常后置注册
                    P_Step1Done = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_Step1_Cube.Complete();
            }

            // [T_Step1] 时限检查
            if (T_Step1_Cube.IsRunning())
            {
                T_Step1_Cube.TickFrame();
                if (T_Step1_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Step1_Cube.Complete();
                }
            }

            // [T_Step2] 前置检查
            if (P_Step1Done && T_Step2_Cube.IsIdle())
            {
                T_Step2_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
                if (ok)
                {
                    // 正常后置注册
                    P_Step2Done = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_Step2_Cube.Complete();
            }

            // [T_Step2] 时限检查
            if (T_Step2_Cube.IsRunning())
            {
                T_Step2_Cube.TickFrame();
                if (T_Step2_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Step2_Cube.Complete();
                }
            }

            // [T_Step3] 前置检查
            if (P_Step2Done && T_Step3_Cube.IsIdle())
            {
                T_Step3_Cube.Start();
                // 执行动作（积木调用）
                bool ok = Mau.Bricks.FileBrick.Convert(_input, _output);
                if (ok)
                {
                    // 正常后置注册
                    P_Step3Done = true;
                }
                else
                {
                    // 错误后置注册（互斥）
                    P_Failed = true;
                }

                // 同步积木当帧完成
                T_Step3_Cube.Complete();
            }

            // [T_Step3] 时限检查
            if (T_Step3_Cube.IsRunning())
            {
                T_Step3_Cube.TickFrame();
                if (T_Step3_Cube.IsExpired())
                {
                    // 超时 → 错误后置
                    P_Failed = true;
                    T_Step3_Cube.Complete();
                }
            }
        }

        // 组合 FL_Main:
        //   序列: T_Step1, T_Step2, T_Step3
        //   重试: 2
        //   汇合: P_Step3Done / P_Failed

        /// <summary>
        /// 查询结果：Step1Done
        /// </summary>
        /// <returns>Step1Done成立</returns>
        public bool IsStep1Done()
        {
            return P_Step1Done;
        }

        /// <summary>
        /// 查询结果：Step2Done
        /// </summary>
        /// <returns>Step2Done成立</returns>
        public bool IsStep2Done()
        {
            return P_Step2Done;
        }

        /// <summary>
        /// 查询结果：Step3Done
        /// </summary>
        /// <returns>Step3Done成立</returns>
        public bool IsStep3Done()
        {
            return P_Step3Done;
        }

        /// <summary>
        /// 查询结果：Failed
        /// </summary>
        /// <returns>Failed成立</returns>
        public bool IsFailed()
        {
            return P_Failed;
        }

    }
}
// #MAU_CHECKSUM:SHA256:586C1F9C7683A7FD76F1D0CF0C240AF45854099D95520A337F8061F52BF8F5F5